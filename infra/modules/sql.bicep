@description('Where the resources go.')
param location string

@description('Suffix that makes globally-unique names unique.')
param suffix string

@description('Tags applied to everything in this module.')
param tags object

@description('Object id of the Entra principal that administers the server.')
param administratorObjectId string

@description('Display name of that principal, for the portal.')
param administratorName string

var databaseName = 'sqldb-statuspage'

resource server 'Microsoft.Sql/servers@2024-05-01-preview' = {
  name: 'sql-statuspage-${suffix}'
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'

    // Entra-only. There is no SQL login and no password anywhere in this deployment, which is
    // the reason no connection string in Key Vault contains one either.
    administrators: {
      administratorType: 'ActiveDirectory'
      login: administratorName
      sid: administratorObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
      principalType: 'User'
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2024-05-01-preview' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    // The free offer only exists on serverless General Purpose. GP_S_Gen5_2 is the shape it
    // is granted against; anything else silently becomes a billable database that looks
    // identical until an invoice arrives.
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    maxSizeBytes: 34359738368

    // 100,000 vCore-seconds a month. At the 0.5-vCore floor that is roughly 55 hours of
    // *awake* database, which is the number the entire read-model design exists to respect.
    useFreeLimit: true

    // AutoPause, never BillOverage. When the allowance runs out the database stops until the
    // month rolls over. That is the correct choice for something that must never be able to
    // generate a bill, and the wrong one for anything real — the trade is deliberate.
    freeLimitExhaustionBehavior: 'AutoPause'

    // Sixty minutes of idle and it sleeps. The checker reads its configuration from blob
    // storage precisely so that it can.
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }
}

// Azure services reaching the server. The Container App has no fixed outbound address on the
// Consumption plan, so a narrower rule would have to be a VNet — which costs money and is a
// larger answer than this question deserves.
resource allowAzure 'Microsoft.Sql/servers/firewallRules@2024-05-01-preview' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name

@description('Entra-authenticated, no password. The identity connecting supplies the credential.')
output connectionString string = 'Server=tcp:${server.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;Authentication=Active Directory Default;'
