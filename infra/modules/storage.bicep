@description('Where the resources go.')
param location string

@description('Suffix that makes globally-unique names unique.')
param suffix string

@description('Tags applied to everything in this module.')
param tags object

@description('Origins allowed to read the snapshot from a browser. The Static Web App is one.')
param allowedOrigins array

@description('Principal id of the workload identity that reads and writes the read model.')
param workloadPrincipalId string

var privateContainer = 'readmodel'
var publicContainer = 'status'

resource account 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  // Storage account names are global, lowercase, and at most 24 characters, which is why this
  // one does not read like the others.
  name: 'ststatuspage${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true

    // One container in this account is meant to be read by anyone, so blob-level anonymous
    // access has to be permitted at the account level before a container can opt into it.
    // Container-level listing stays off everywhere: a reader may fetch status.json, and may
    // not enumerate what else is there.
    allowBlobPublicAccess: true

    // Nothing in this system authenticates to storage with a key. The API and the checker
    // both use their managed identities, so leaving key access on would only preserve a
    // credential nobody uses and everybody could leak.
    allowSharedKeyAccess: false
    accessTier: 'Hot'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

resource blob 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: account
  name: 'default'
  properties: {
    // Without this the page cannot read the snapshot at all. It is served from the Static Web
    // App's origin and the blob is on another, so the browser needs
    // Access-Control-Allow-Origin on the response — anonymous public read is not enough.
    // This was found the hard way locally, where Azurite starts with no rules either.
    cors: {
      corsRules: [
        {
          allowedOrigins: allowedOrigins
          // Read only. Nothing a browser does to this account should ever be a write.
          allowedMethods: ['GET', 'HEAD']
          allowedHeaders: ['*']
          exposedHeaders: ['*']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

// config.json and checker-state.json. Private: config describes what is monitored and where.
resource readModel 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blob
  name: privateContainer
  properties: { publicAccess: 'None' }
}

// status.json, and nothing else. Blob-level access, not Container: a reader may fetch a blob
// by name and may not list the container's contents.
resource status 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blob
  name: publicContainer
  properties: { publicAccess: 'Blob' }
}

// Declared here rather than in the parent. A role assignment's name and scope must be
// computable at the start of the deployment, and a module output is not — so the grant lives
// beside the resource it is about (BCP120).
//
// Data plane only. The identity reads and writes blobs; it cannot rotate keys, change the CORS
// rules this template declares, or alter a container's public access.
resource canWriteBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: account
  name: guid(account.id, workloadPrincipalId, 'Storage Blob Data Contributor')
  properties: {
    principalId: workloadPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    )
  }
}

output accountName string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
output snapshotUrl string = '${account.properties.primaryEndpoints.blob}${publicContainer}/status.json'
output accountId string = account.id
