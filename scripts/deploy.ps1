#Requires -Version 7
<#
.SYNOPSIS
    Creates or updates the environment, migrates the database, and verifies the result.

.DESCRIPTION
    Everything the deployment needs is either in infra/, derived from the signed-in principal,
    or generated here. There is nothing to fill in by hand and nothing to remember.

    The signing key is generated once and kept in Key Vault. On a redeploy the existing key is
    read back rather than replaced, because rotating it would sign out every operator for no
    reason — a redeploy is not a security event.

    The database is Entra-only, so the workload identity needs a contained user before it can
    connect. That is a step the template cannot do: it needs a connection to the database as
    the Entra admin, which is a person rather than a resource. scripts/grant-database-access.ps1
    does it and this script calls it.

.PARAMETER ResourceGroup
    Resource group to deploy into. Created if it does not exist.

.PARAMETER Location
    Region for everything except the Static Web App.

.PARAMETER SkipGrant
    Skip the database grant. Only useful when re-running after it has already succeeded.

.PARAMETER SkipSite
    Skip building and uploading the status page. The slowest step and the one least likely to
    have changed when re-running to fix something in the infrastructure.

.PARAMETER ImageTag
    Tag to deploy for all three images. Omit it and the template's default applies, which is
    the mutable 'latest'.

    CI passes the commit sha, and that is the case worth having. A deployment tracking 'latest'
    cannot say which build is running: pushing a new image does not roll the app, because the
    revision's image string has not changed and Container Apps has no reason to pull again. For
    a project whose claim is that the container CI built and tested is the one running in
    production, an artefact nobody can name does not support the claim.

.PARAMETER ImageRepository
    Where the images live, without the image name or tag.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-statuspage',
    [string] $Location = 'swedencentral',
    [switch] $SkipGrant,
    [switch] $SkipSite,
    [string] $ImageTag,
    [string] $ImageRepository = 'ghcr.io/juliannordin/statuspage'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step { param([string] $Message) Write-Host ''; Write-Host "==> $Message" -ForegroundColor Cyan }

Write-Step 'Checking the signed-in principal'
$account = az account show --output json | ConvertFrom-Json

# Whoever deploys becomes the database's Entra admin, and then does the grant as that admin,
# which keeps the two consistent however this script was started. The two ways of signing in
# answer "who are you" differently though: a person has a signed-in user, and a workflow
# holding a federated credential has none — `az ad signed-in-user show` fails outright there
# rather than returning nothing. So ask the account what kind it is instead of assuming.
if ($account.user.type -eq 'servicePrincipal') {
    $servicePrincipal = az ad sp show --id $account.user.name --output json | ConvertFrom-Json
    $adminObjectId = $servicePrincipal.id
    $adminName = $servicePrincipal.displayName
}
else {
    $principal = az ad signed-in-user show --output json | ConvertFrom-Json
    $adminObjectId = $principal.id
    $adminName = $principal.userPrincipalName
}

Write-Host "    subscription  $($account.name)"
Write-Host "    principal     $adminName"

Write-Step "Ensuring $ResourceGroup exists"
az group create --name $ResourceGroup --location $Location --tags project=statuspage managedBy=bicep --output none

# Read the key back if it is already there. A redeploy is not a reason to sign everybody out.
Write-Step 'Resolving the JWT signing key'
$vaultName = az keyvault list --resource-group $ResourceGroup --query '[0].name' --output tsv 2>$null
$signingKey = $null
if ($vaultName) {
    $signingKey = az keyvault secret show --vault-name $vaultName --name jwt-signing-key --query value --output tsv 2>$null
}
if ($signingKey) {
    Write-Host '    reusing the existing key'
}
else {
    # 48 random bytes, base64. Comfortably past the 32-character minimum the options enforce.
    $bytes = [byte[]]::new(48)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $signingKey = [Convert]::ToBase64String($bytes)
    Write-Host '    generated a new key'
}

# Same treatment for the operator password, for a sharper reason. Regenerating it on a
# redeploy would lock out the only account that can declare an incident — and it would not
# even replace anything, because the seeder never re-passwords an account that already
# exists. The new value would simply stop matching the one that works.
Write-Step 'Resolving the operator password'
$operatorPassword = $null
if ($vaultName) {
    $operatorPassword = az keyvault secret show --vault-name $vaultName --name operator-password --query value --output tsv 2>$null
}
if ($operatorPassword) {
    Write-Host '    reusing the existing password'
}
else {
    # Identity's default policy wants an uppercase letter, a lowercase one, a digit and a
    # symbol, and base64 on its own guarantees none of the four. The suffix guarantees all of
    # them; the random part in front is what actually carries the entropy. Getting this wrong
    # fails at seeding time, on a deployment that otherwise looks finished.
    $bytes = [byte[]]::new(24)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $operatorPassword = [Convert]::ToBase64String($bytes) + 'Aa1!'
    Write-Host '    generated a new password'
}

$env:STATUSPAGE_ADMIN_OBJECT_ID = $adminObjectId
$env:STATUSPAGE_ADMIN_NAME = $adminName
$env:STATUSPAGE_JWT_SIGNING_KEY = $signingKey
$env:STATUSPAGE_OPERATOR_PASSWORD = $operatorPassword

if ($ImageTag) {
    Write-Step "Pinning images to $ImageTag"
    $env:STATUSPAGE_API_IMAGE = "$ImageRepository/api:$ImageTag"
    $env:STATUSPAGE_CHECKER_IMAGE = "$ImageRepository/checker:$ImageTag"
    $env:STATUSPAGE_MIGRATE_IMAGE = "$ImageRepository/migrate:$ImageTag"
    Write-Host "    $($env:STATUSPAGE_API_IMAGE)"
}

Write-Step 'Deploying the template'
$deployment = az deployment group create `
    --resource-group $ResourceGroup `
    --name "statuspage-$(Get-Date -Format 'yyyyMMddHHmmss')" `
    --template-file (Join-Path $repoRoot 'infra/main.bicep') `
    --parameters (Join-Path $repoRoot 'infra/main.bicepparam') `
    --output json | ConvertFrom-Json

$out = $deployment.properties.outputs
Write-Host "    api      $($out.apiUrl.value)"
Write-Host "    site     $($out.siteUrl.value)"
Write-Host "    snapshot $($out.snapshotUrl.value)"

# Read back what is running rather than trusting that the parameter arrived. This is also the
# only place the running artefact gets named out loud, which is the point of pinning a tag: a
# deployment that cannot say which build it is serving cannot claim CI proved that build.
$runningImage = az containerapp show `
    --name $out.apiName.value `
    --resource-group $ResourceGroup `
    --query 'properties.template.containers[0].image' --output tsv
Write-Host "    running  $runningImage"

if ($ImageTag -and $runningImage -ne $env:STATUSPAGE_API_IMAGE) {
    throw "asked for $($env:STATUSPAGE_API_IMAGE) and the app is running $runningImage"
}

if (-not $SkipGrant) {
    Write-Step 'Granting the workload identity access to the database'
    & (Join-Path $PSScriptRoot 'grant-database-access.ps1') `
        -ResourceGroup $ResourceGroup `
        -ServerName $out.sqlServerName.value `
        -ServerFqdn $out.sqlServerFqdn.value `
        -Database $out.sqlDatabaseName.value `
        -IdentityName $out.identityName.value
}

Write-Step 'Running migrations'
# Started and waited on. A failed migration has to stop the deployment rather than leave the
# API to crash-loop against a schema that is half there.
$started = az containerapp job start `
    --name $out.migrateJobName.value `
    --resource-group $ResourceGroup `
    --output json | ConvertFrom-Json

# Watch this execution by name. Reading [0] of the execution list is only correct while the
# job has never run before: every previous attempt stays in that list, nothing promises the
# newest is first, and a stale Failed read as the current one would abort a deploy that was
# actually fine. It is right for exactly as long as it is never needed.
$executionName = $started.name
if (-not $executionName) { throw 'Could not determine which migration execution was started.' }
Write-Host "    execution $executionName"

# Waiting on terminal states rather than on a list of running ones: an execution that is
# queued reports a status this script has never heard of, and treating unrecognised as
# finished would call a job that had not started yet a failure.
$deadline = (Get-Date).AddMinutes(10)
do {
    Start-Sleep -Seconds 10
    $status = az containerapp job execution show `
        --name $out.migrateJobName.value `
        --resource-group $ResourceGroup `
        --job-execution-name $executionName `
        --query 'properties.status' --output tsv
    Write-Host "    $status"
} while ($status -notin @('Succeeded', 'Failed', 'Cancelled') -and (Get-Date) -lt $deadline)

if ($status -ne 'Succeeded') {
    Write-Host "  FAIL  migration job ended as $status" -ForegroundColor Red
    az containerapp job logs show `
        --name $out.migrateJobName.value `
        --resource-group $ResourceGroup `
        --container migrate `
        --execution $executionName `
        --tail 50
    exit 1
}

if (-not $SkipSite) {
    # Before the smoke test rather than after it, so the checks run against the site this
    # deployment actually published rather than whatever was there beforehand.
    Write-Step 'Publishing the status page'
    & (Join-Path $PSScriptRoot 'publish-site.ps1') `
        -ResourceGroup $ResourceGroup `
        -SiteName $out.siteName.value `
        -ApiUrl $out.apiUrl.value `
        -SnapshotUrl $out.snapshotUrl.value
}

Write-Step 'Smoke test'
& (Join-Path $PSScriptRoot 'smoke.ps1') `
    -ApiUrl $out.apiUrl.value `
    -SnapshotUrl $out.snapshotUrl.value `
    -SiteUrl $out.siteUrl.value `
    -OperatorEmail $out.operatorEmail.value `
    -OperatorPassword $operatorPassword

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
Write-Host "  Status page  $($out.siteUrl.value)"
Write-Host "  API          $($out.apiUrl.value)"
Write-Host "  Operator     $($out.operatorEmail.value)"
# The command rather than the value. This script runs in CI too, and stdout there is a build
# log that outlives the deployment and is readable by anyone who can read the repository.
Write-Host "               az keyvault secret show --vault-name $($out.vaultName.value) --name operator-password --query value -o tsv"
