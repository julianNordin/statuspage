#Requires -Version 7
<#
.SYNOPSIS
    Deletes the environment and verifies nothing is left behind.

.DESCRIPTION
    Everything is described by infra/main.bicep, so destroying this costs nothing that cannot
    be rebuilt with one command, which is the actual payoff of having written it as code.

    Two things this does that a bare `az group delete` does not. It reads the Key Vault name
    before deleting the group, because afterwards there is nothing left to ask. And it purges
    the vault, because soft delete keeps the name reserved for the whole retention period and
    the name is derived from the resource group id, so a rebuild lands on exactly the same one
    and fails with a name-in-use error that points nowhere near the real cause.

    Worth knowing before running this: the Azure SQL free offer is one database per
    subscription. Tearing this down releases the slot, and standing another project up takes
    it. P6 and P12 cannot both be deployed without one of them paying.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-statuspage',
    [string] $Location = 'swedencentral',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string] $Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

Write-Step "Checking $ResourceGroup"
if (-not (az group exists --name $ResourceGroup | ConvertFrom-Json)) {
    Write-Host "Resource group $ResourceGroup does not exist. Nothing to do."
    exit 0
}

$resources = az resource list --resource-group $ResourceGroup --query '[].{name:name, type:type}' --output json | ConvertFrom-Json
Write-Host "  $(@($resources).Count) resource(s) will be deleted:"
$resources | ForEach-Object { Write-Host "    $($_.type)/$($_.name)" }

if (-not $Force) {
    $answer = Read-Host "`nDelete resource group '$ResourceGroup' and everything in it? Type the group name to confirm"
    if ($answer -ne $ResourceGroup) {
        Write-Host 'Aborted.' -ForegroundColor Yellow
        exit 1
    }
}

Write-Step 'Recording the Key Vault name before it disappears'
$vaultNames = az keyvault list --resource-group $ResourceGroup --query '[].name' --output json | ConvertFrom-Json
if (@($vaultNames).Count -gt 0) {
    $vaultNames | ForEach-Object { Write-Host "    $_" }
}
else {
    Write-Host '    none found'
}

Write-Step "Deleting $ResourceGroup"
az group delete --name $ResourceGroup --yes --no-wait
Write-Host '    delete requested; waiting for it to finish'
az group wait --name $ResourceGroup --deleted --timeout 1800

Write-Step 'Purging soft-deleted Key Vaults'
foreach ($vaultName in $vaultNames) {
    # Purge protection is deliberately off in the template precisely so this can succeed.
    Write-Host "    purging $vaultName"
    az keyvault purge --name $vaultName --location $Location --no-wait
}

Write-Step 'Verifying'
if (az group exists --name $ResourceGroup | ConvertFrom-Json) {
    Write-Host "  FAIL  resource group $ResourceGroup still exists" -ForegroundColor Red
    exit 1
}
Write-Host "  ok    resource group $ResourceGroup is gone" -ForegroundColor Green

Write-Host ''
Write-Host 'Environment destroyed. Nothing is accruing.' -ForegroundColor Green
Write-Host 'Rebuild it with: ./scripts/deploy.ps1'
