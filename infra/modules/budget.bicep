@description('Name of the budget. Scoped to the resource group this is deployed into.')
param name string = 'budget-statuspage'

@description('Monthly ceiling, in the subscription billing currency. Not a limit — Azure cannot stop spending — but the number that decides when somebody is told.')
param amount int = 10

@description('First day of the first month the budget covers. Must be the first of a month; defaults to the current one.')
param startDate string = '${utcNow('yyyy-MM')}-01'

@description('Addresses told when a threshold trips. Empty means notify by role instead, which is the default and keeps addresses out of the template.')
param contactEmails array = []

// Everything here is designed to cost nothing: Container Apps and Static Web Apps have free
// grants this workload fits inside with room to spare, the database is on the free offer, and
// blob traffic is measured in kilobytes. So this is not a budget in the ordinary sense of
// rationing a known spend. It is a smoke alarm.
//
// The failure it exists for is the silent one. Every free allowance here is free *until a flag
// is wrong* — a database created without useFreeLimit, an image registry that is not GHCR, a
// checker whose cron is edited from ten minutes to one and quietly burns the Container Apps
// grant in a week. None of those announce themselves, and the first honest signal is an
// invoice. Ten of anything is far enough above zero to be deliberate and far enough below a
// real bill to arrive while it still matters.
//
// Forecast first, actual second. A forecast breach is the one that leaves time to act; by the
// time actual spend crosses the line the money is already gone.
resource budget 'Microsoft.Consumption/budgets@2021-10-01' = {
  name: name
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      forecastOverEighty: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Forecasted'
        contactEmails: contactEmails
        // By role rather than by address, so no mailbox is named in a public template and the
        // alert follows whoever owns the subscription rather than whoever wrote this.
        contactRoles: empty(contactEmails) ? ['Owner'] : []
      }
      actualOverHundred: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: contactEmails
        contactRoles: empty(contactEmails) ? ['Owner'] : []
      }
    }
  }
}

output budgetName string = budget.name
output budgetAmount int = amount
