@description('Where the resources go.')
param location string

@description('Suffix that makes globally-unique names unique.')
param suffix string

@description('Tags applied to everything in this module.')
param tags object

@description('Log Analytics workspace the environment writes to.')
param workspaceCustomerId string

@description('Shared key for that workspace.')
@secure()
param workspaceSharedKey string

@description('Resource id of the workload identity. Created by the parent, so that storage and the vault can grant to it without waiting on this module.')
param identityId string

@description('Client id of that identity, for DefaultAzureCredential.')
param identityClientId string

@description('Application Insights connection string.')
@secure()
param insightsConnectionString string

@description('Fully qualified image for the API.')
param apiImage string

@description('Fully qualified image for the checker.')
param checkerImage string

@description('Fully qualified image for the migration bundle.')
param migrateImage string

@description('Entra-authenticated connection string. Contains no password.')
param sqlConnectionString string

@description('Blob endpoint of the read model storage account.')
param blobEndpoint string

@description('Key Vault URI holding the JWT signing key.')
param vaultUri string

@description('Origins the API accepts cross-origin calls from.')
param allowedOrigins array

@description('Registry the images come from. Empty for a public registry.')
param registryServer string = ''

@description('How often the checker runs, as a cron expression.')
param checkerCron string = '*/10 * * * *'

@description('Sign-in address of the seeded operator. There is no registration endpoint, so an account that is not seeded cannot come into being at all.')
param operatorEmail string

@description('Name shown beside incident updates posted by that operator.')
param operatorDisplayName string

@description('Components a fresh deployment starts with. Matched on slug and never updated afterwards, so an operator who retunes or deletes one is not argued with on the next restart. Change these to change what the page watches.')
param seededComponents array = [
  {
    name: 'GitHub API'
    slug: 'github-api'
    targetUrl: 'https://api.github.com'
    position: 0
  }
  {
    name: 'NuGet'
    slug: 'nuget'
    targetUrl: 'https://api.nuget.org/v3/index.json'
    position: 1
  }
]

// Flattened into the numbered keys .NET configuration binds an array from. Written out here
// rather than in the image so that what this deployment watches is a property of the
// deployment, and changing it is a parameter rather than a rebuild.
var seededComponentEnv = flatten(map(range(0, length(seededComponents)), i => [
  { name: 'Components__${i}__Name', value: seededComponents[i].name }
  { name: 'Components__${i}__Slug', value: seededComponents[i].slug }
  { name: 'Components__${i}__TargetUrl', value: seededComponents[i].targetUrl }
  { name: 'Components__${i}__Position', value: string(seededComponents[i].position) }
]))

var jwtIssuer = 'statuspage'
var jwtAudience = 'statuspage-console'

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-statuspage-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspaceCustomerId
        sharedKey: workspaceSharedKey
      }
    }
    zoneRedundant: false
  }
}

var identityConfig = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${identityId}': {} }
}

var registries = empty(registryServer) ? [] : [
  {
    server: registryServer
    identity: identityId
  }
]

var sharedEnv = [
  { name: 'ConnectionStrings__Default', value: sqlConnectionString }
  { name: 'ReadModel__ServiceUri', value: blobEndpoint }
  { name: 'ReadModel__PrivateContainer', value: 'readmodel' }
  { name: 'ReadModel__PublicContainer', value: 'status' }
  // The template declares the storage account's CORS rules and the app's identity cannot
  // change them, so the app must not try.
  { name: 'ReadModel__ConfigureCors', value: 'false' }
  { name: 'AZURE_CLIENT_ID', value: identityClientId }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'insights-connection' }
]

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-statuspage-api-${suffix}'
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [{ latestRevision: true, weight: 100 }]
      }
      registries: registries
      secrets: [
        { name: 'insights-connection', value: insightsConnectionString }
        // Resolved from Key Vault at start-up by the identity below. The value never appears
        // in the template, in a parameter file, or in the portal's settings blade.
        {
          name: 'jwt-signing-key'
          keyVaultUrl: '${vaultUri}secrets/jwt-signing-key'
          identity: identityId
        }
        {
          name: 'operator-password'
          keyVaultUrl: '${vaultUri}secrets/operator-password'
          identity: identityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(sharedEnv, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // A one-shot job migrates. Doing it here as well would be a race the moment a
            // second replica exists.
            { name: 'Database__MigrateOnStartup', value: 'false' }
            { name: 'Jwt__Issuer', value: jwtIssuer }
            { name: 'Jwt__Audience', value: jwtAudience }
            { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
            { name: 'Cors__AllowedOrigins__0', value: allowedOrigins[0] }
            // Without these the Operators section stays the empty array from appsettings.json,
            // the seeder is never called, and the deployment comes up with no account that can
            // sign in — so no components, so nothing for the checker to check and nothing for
            // the page to show. It looks like a working deployment from the outside, which is
            // exactly why the smoke test now signs in rather than assuming.
            { name: 'Operators__0__Email', value: operatorEmail }
            { name: 'Operators__0__DisplayName', value: operatorDisplayName }
            { name: 'Operators__0__Password', secretRef: 'operator-password' }
          ], seededComponentEnv)
          probes: [
            {
              type: 'Readiness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        // Scale to zero. An idle API costs nothing, which is most of why this deployment fits
        // inside the free grant; the price is a cold start on the first request after a quiet
        // spell, and the public page does not pay it because it never calls this.
        minReplicas: 0
        maxReplicas: 2
        rules: [
          {
            name: 'http'
            http: { metadata: { concurrentRequests: '20' } }
          }
        ]
      }
    }
  }
}

// The checker. A cron job rather than a running service: it wakes, checks, writes what
// changed and exits, and is billed only for the seconds it was awake.
resource checker 'Microsoft.App/jobs@2025-01-01' = {
  name: 'caj-statuspage-checker-${suffix}'
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 300
      replicaRetryLimit: 1
      scheduleTriggerConfig: {
        cronExpression: checkerCron
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registries
      secrets: [{ name: 'insights-connection', value: insightsConnectionString }]
    }
    template: {
      containers: [
        {
          name: 'checker'
          image: checkerImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat(sharedEnv, [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            // One shot. The platform's schedule is the loop.
            { name: 'Checker__Loop', value: 'false' }
          ])
        }
      ]
    }
  }
}

// Migrations. Manual trigger, started by the deployment and waited on, so a failed migration
// stops the deploy instead of leaving the API to crash-loop against half a schema.
resource migrate 'Microsoft.App/jobs@2025-01-01' = {
  name: 'caj-statuspage-migrate-${suffix}'
  location: location
  tags: tags
  identity: identityConfig
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: migrateImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          // AZURE_CLIENT_ID is not optional here and its absence is not obvious. With a
          // user-assigned identity the credential chain cannot tell which identity to use
          // and fails with "Unable to load the proper Managed Identity" — which reads like a
          // permissions problem and is not one. The first real deployment failed exactly
          // here, because this container had args and no environment at all.
          env: [
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
          ]
          args: ['--connection', sqlConnectionString]
        }
      ]
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output apiName string = api.name
output migrateJobName string = migrate.name
output checkerJobName string = checker.name
output environmentId string = environment.id
