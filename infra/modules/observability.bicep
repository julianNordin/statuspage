@description('Where the resources go.')
param location string

@description('Suffix that makes globally-unique names unique.')
param suffix string

@description('Tags applied to everything in this module.')
param tags object

// Container Apps requires a Log Analytics workspace, so this exists whether or not anybody
// reads it. Thirty days is the free retention; longer would start costing money to keep logs
// nobody has looked at since the incident they were written for.
resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-statuspage-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    features: { enableLogAccessUsingOnlyResourcePermissions: true }
  }
}

// Workspace-based Application Insights. The classic mode is retired, and the workspace is
// already here for Container Apps, so this costs one resource and no extra ingestion tier.
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-statuspage-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId

// listKeys has to run where the resource is declared. Called on a module output in the parent
// it is not computable at the start of the deployment, and Bicep refuses it (BCP181).
@secure()
output workspaceSharedKey string = workspace.listKeys().primarySharedKey
output insightsConnectionString string = insights.properties.ConnectionString
