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

// Saved with the workspace rather than written down in a runbook nobody opens. Each one
// answers a question this project has actually had to ask, and every one of them was executed
// against this workspace before being saved here. That is not ceremony: the platform query was
// wrong when first written — it matched every successful job exit — and only running it showed
// that. The two that return nothing today return nothing because no cycle has failed and no
// incident has opened itself, which is the answer, not a broken query.
var savedQueries = [
  {
    name: 'checker-woke-the-database'
    displayName: 'Checker: cycles, and which ones woke the database'
    // The claim the whole read model exists for, and the only honest way to check it. A cycle
    // that found no state change must not open a SQL connection at all, because the free offer
    // meters awake time and auto-pause needs sixty unbroken idle minutes. If WokeTheDatabase
    // ever approaches Cycles, the configuration document is not being read and the database is
    // being kept awake by the very job that was designed to let it sleep.
    query: '''
ContainerAppConsoleLogs_CL
| where ContainerName_s == "checker"
| extend log = parse_json(Log_s)
| where tostring(log.EventId.Name) == "CycleFinished"
| summarize Cycles = count(), WokeTheDatabase = countif(tobool(log.Touched)) by bin(TimeGenerated, 1h)
| order by TimeGenerated desc
'''
  }
  {
    name: 'component-state-changes'
    displayName: 'Checker: every state transition'
    // The interval log in narrative form. Hysteresis means a transition here has already
    // survived N consecutive observations, so each row is a real change rather than a blip.
    query: '''
ContainerAppConsoleLogs_CL
| where ContainerName_s == "checker"
| extend log = parse_json(Log_s)
| where tostring(log.EventId.Name) == "StateChanged"
| project TimeGenerated, Slug = tostring(log.Slug), From = tostring(log.From), To = tostring(log.To)
| order by TimeGenerated desc
'''
  }
  {
    name: 'incidents-opened-automatically'
    displayName: 'Checker: incidents it opened by itself'
    // Opening is automatic, resolving is not, so this is the list of things a person still
    // owes a decision on. An empty result and an open incident on the page mean somebody
    // opened it by hand.
    query: '''
ContainerAppConsoleLogs_CL
| where ContainerName_s == "checker"
| extend log = parse_json(Log_s)
| where tostring(log.EventId.Name) == "IncidentOpened"
| project TimeGenerated, Slug = tostring(log.Slug), CorrelationId = tostring(log.CorrelationId)
| order by TimeGenerated desc
'''
  }
  {
    name: 'checker-failures'
    displayName: 'Checker: cycles that threw'
    // A failed cycle exits non-zero and the platform records the execution as Failed, but the
    // reason only exists here. Correlate on CorrelationId to get the whole run.
    query: '''
ContainerAppConsoleLogs_CL
| where ContainerName_s == "checker"
| extend log = parse_json(Log_s)
| where tostring(log["@l"]) in ("Error", "Fatal")
| project TimeGenerated, Message = tostring(log["@mt"]), Exception = tostring(log["@x"]), CorrelationId = tostring(log.CorrelationId)
| order by TimeGenerated desc
'''
  }
  {
    name: 'container-restarts-and-failures'
    displayName: 'Platform: containers that terminated badly'
    // Not application logs. This is where an image that will not pull, a container killed for
    // memory, or a job whose replica never started shows up — all of which look like silence
    // in the console logs rather than like an error.
    //
    // The exclusions are the whole query. Without them it returns every successful job run
    // (a cron job exits and reports ContainerTerminated with code 0) and every revision
    // rollover (a redeploy stops the old replica as ManuallyStopped), which is hundreds of
    // rows of nothing and one real failure somewhere inside them. Narrowed like this against
    // this workspace it returned exactly one row: the migration container exiting 1 on the
    // first deployment, which is precisely the event worth finding.
    query: '''
ContainerAppSystemLogs_CL
| where Reason_s in ("ContainerCrashed", "PullImageFailed", "Killing", "BackOff")
    or (Reason_s == "ContainerTerminated" and Log_s !contains "exit code '0'" and Log_s !contains "ManuallyStopped")
| project TimeGenerated, Reason_s, Log_s
| order by TimeGenerated desc
'''
  }
]

resource searches 'Microsoft.OperationalInsights/workspaces/savedSearches@2023-09-01' = [
  for q in savedQueries: {
    parent: workspace
    name: q.name
    properties: {
      category: 'statuspage'
      displayName: q.displayName
      query: q.query
    }
  }
]

output workspaceId string = workspace.id
output workspaceCustomerId string = workspace.properties.customerId

// listKeys has to run where the resource is declared. Called on a module output in the parent
// it is not computable at the start of the deployment, and Bicep refuses it (BCP181).
@secure()
output workspaceSharedKey string = workspace.listKeys().primarySharedKey
output insightsConnectionString string = insights.properties.ConnectionString
