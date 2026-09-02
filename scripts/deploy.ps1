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
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-statuspage',
    [string] $Location = 'swedencentral',
    [switch] $SkipGrant
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Write-Step { param([string] $Message) Write-Host ''; Write-Host "==> $Message" -ForegroundColor Cyan }

Write-Step 'Checking the signed-in principal'
$account = az account show --output json | ConvertFrom-Json
$principal = az ad signed-in-user show --output json | ConvertFrom-Json
Write-Host "    subscription  $($account.name)"
Write-Host "    principal     $($principal.userPrincipalName)"

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

$env:STATUSPAGE_ADMIN_OBJECT_ID = $principal.id
$env:STATUSPAGE_ADMIN_NAME = $principal.userPrincipalName
$env:STATUSPAGE_JWT_SIGNING_KEY = $signingKey

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
az containerapp job start `
    --name $out.migrateJobName.value `
    --resource-group $ResourceGroup `
    --output none

$deadline = (Get-Date).AddMinutes(10)
do {
    Start-Sleep -Seconds 10
    $execution = az containerapp job execution list `
        --name $out.migrateJobName.value `
        --resource-group $ResourceGroup `
        --query '[0].{status:properties.status, name:name}' --output json | ConvertFrom-Json
    Write-Host "    $($execution.status)"
} while ($execution.status -in @('Running', 'Processing') -and (Get-Date) -lt $deadline)

if ($execution.status -ne 'Succeeded') {
    Write-Host "  FAIL  migration job ended as $($execution.status)" -ForegroundColor Red
    az containerapp job logs show --name $out.migrateJobName.value --resource-group $ResourceGroup --container migrate --tail 50
    exit 1
}

Write-Step 'Smoke test'
& (Join-Path $PSScriptRoot 'smoke.ps1') `
    -ApiUrl $out.apiUrl.value `
    -SnapshotUrl $out.snapshotUrl.value `
    -SiteUrl $out.siteUrl.value

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
Write-Host "  Status page  $($out.siteUrl.value)"
Write-Host "  API          $($out.apiUrl.value)"
