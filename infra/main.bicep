targetScope = 'resourceGroup'

metadata description = '''
The whole environment. Container Apps for the API and the checker, Static Web Apps for the
page, SQL on the free offer, blob storage for the read model, Key Vault for the one secret
there is, and Application Insights over the top.

Everything here is free at this scale, and the shapes that make it free are load-bearing
rather than incidental: the database is the serverless free offer with AutoPause on
exhaustion, the API scales to zero, and the checker is a cron job billed by the second. The
one resource that would quietly start a bill — a container registry — is deliberately absent;
images come from GHCR, which is free for public packages.
'''

@description('Region for everything except the static site.')
param location string = resourceGroup().location

@description('''
Where the Static Web App resource lives. It is NOT available everywhere: the control plane
exists only in centralus, eastus2, westus2, westeurope and eastasia, and asking for anywhere
else fails with LocationNotAvailableForResourceType.

westeurope would be the obvious choice from Sweden and this subscription is refused there:
"The selected region is currently not accepting new customers". eastus2 accepts it. That
costs nothing measurable, because this is a control-plane location only — the site is served
from a global CDN, and a reader in Stockholm is not fetching it from Virginia.
''')
@allowed(['centralus', 'eastus2', 'westus2', 'westeurope', 'eastasia'])
param staticSiteLocation string = 'eastus2'

@description('Object id of the principal deploying this. Becomes the SQL admin and can write the vault secret.')
param administratorObjectId string

@description('Display name of that principal, shown in the portal.')
param administratorName string

@description('JWT signing key. Generated per environment and never committed.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Fully qualified image for the API.')
param apiImage string

@description('Fully qualified image for the checker.')
param checkerImage string

@description('Fully qualified image for the migration bundle.')
param migrateImage string

@description('Registry the images come from. Empty for a public one.')
param registryServer string = ''

@description('How often the checker runs.')
param checkerCron string = '*/10 * * * *'

// Derived from the resource group's id, so a rebuild into the same group lands on the same
// names and a rebuild into a different one does not collide.
var suffix = take(uniqueString(resourceGroup().id), 8)

var tags = {
  project: 'statuspage'
  managedBy: 'bicep'
}

// Created before anything that grants to it. Storage and the vault each declare their own
// role assignment, and a role assignment's scope has to be a resource this template knows
// about at the start of the deployment rather than a module output (BCP120). Putting the
// identity here is what breaks the cycle: the grants depend on it, and it depends on nothing.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-statuspage-${suffix}'
  location: location
  tags: tags
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: { location: location, suffix: suffix, tags: tags }
}

// Created before storage, because storage needs to know which origin may read the snapshot
// and that origin is this resource's hostname.
resource staticSite 'Microsoft.Web/staticSites@2024-11-01' = {
  name: 'stapp-statuspage-${suffix}'
  location: staticSiteLocation
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {
    // Deployed by the pipeline with a token rather than wired to a repository here. Putting a
    // repository URL in the template would make this resource refuse to exist until one does.
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

var siteOrigin = 'https://${staticSite.properties.defaultHostname}'

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    suffix: suffix
    tags: tags
    allowedOrigins: [siteOrigin]
    workloadPrincipalId: identity.properties.principalId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    suffix: suffix
    tags: tags
    administratorObjectId: administratorObjectId
    administratorName: administratorName
  }
}

module vault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    suffix: suffix
    tags: tags
    administratorObjectId: administratorObjectId
    jwtSigningKey: jwtSigningKey
    workloadPrincipalId: identity.properties.principalId
  }
}

module apps 'modules/containerapps.bicep' = {
  name: 'containerapps'
  params: {
    location: location
    suffix: suffix
    tags: tags
    workspaceCustomerId: observability.outputs.workspaceCustomerId
    workspaceSharedKey: observability.outputs.workspaceSharedKey
    identityId: identity.id
    identityClientId: identity.properties.clientId
    insightsConnectionString: observability.outputs.insightsConnectionString
    apiImage: apiImage
    checkerImage: checkerImage
    migrateImage: migrateImage
    registryServer: registryServer
    checkerCron: checkerCron
    sqlConnectionString: sql.outputs.connectionString
    blobEndpoint: storage.outputs.blobEndpoint
    vaultUri: vault.outputs.vaultUri
    allowedOrigins: [siteOrigin]
  }
}

// ---- outputs the deploy script and the smoke test read -------------------------------------

output apiUrl string = apps.outputs.apiUrl
output siteUrl string = siteOrigin
output siteName string = staticSite.name
output snapshotUrl string = storage.outputs.snapshotUrl
output blobEndpoint string = storage.outputs.blobEndpoint
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output vaultName string = vault.outputs.vaultName
output migrateJobName string = apps.outputs.migrateJobName
output checkerJobName string = apps.outputs.checkerJobName
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
