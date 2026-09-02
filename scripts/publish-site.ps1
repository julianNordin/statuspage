#Requires -Version 7
<#
.SYNOPSIS
    Builds the Angular client and uploads it to the Static Web App.

.DESCRIPTION
    One bundle, configured where it is deployed. That is the whole reason the app reads
    config.json at startup instead of having its URLs compiled in: the artefact that was
    tested is the artefact that ships.

    Which makes this the step that has to write config.json, and the reason it is a script
    rather than a line in a README. The copy in client/public is the one local development
    uses and it points at Azurite and localhost. Shipping it unchanged would leave the
    deployed page fetching 127.0.0.1 — and because a failed fetch there deliberately falls
    back to same-origin defaults rather than blanking the page, the result is a site that
    looks perfectly deployed and reports nothing at all. So the file is overwritten in the
    build output, after the build and before the upload, and read back afterwards.

.PARAMETER ResourceGroup
    Resource group holding the Static Web App.

.PARAMETER SiteName
    Name of the Static Web App resource.

.PARAMETER ApiUrl
    Origin of the deployed API. The operator console calls it; the public page never does.

.PARAMETER SnapshotUrl
    Full URL of status.json in blob storage. The public page reads this and nothing else.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $SiteName,
    [Parameter(Mandatory)] [string] $ApiUrl,
    [Parameter(Mandatory)] [string] $SnapshotUrl
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$clientDir = Join-Path $repoRoot 'client'
$outputDir = Join-Path $clientDir 'dist/client/browser'

Push-Location $clientDir
try {
    Write-Host '    installing client dependencies'
    npm ci --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }

    Write-Host '    building'
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "the client build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

if (-not (Test-Path $outputDir)) { throw "the build produced nothing at $outputDir" }

# The template's output is the bare origin; every call the console makes is built by appending
# to this, and the routes it appends already start at /api.
$config = [ordered]@{
    snapshotUrl = $SnapshotUrl
    apiUrl      = "$($ApiUrl.TrimEnd('/'))/api"
}

$configPath = Join-Path $outputDir 'config.json'
$config | ConvertTo-Json | Set-Content -Path $configPath -Encoding utf8
Write-Host "    wrote config.json pointing at $($config.apiUrl)"

# Read back rather than trust the write. A config.json that did not land is the one failure
# this script exists to prevent, and it is the one failure that is invisible in the result.
$written = Get-Content $configPath -Raw | ConvertFrom-Json
if ($written.snapshotUrl -ne $SnapshotUrl) { throw 'config.json did not land as written' }

Write-Host '    uploading'
$token = az staticwebapp secrets list `
    --name $SiteName `
    --resource-group $ResourceGroup `
    --query 'properties.apiKey' --output tsv
if (-not $token) { throw "could not read a deployment token for $SiteName" }

npx --yes @azure/static-web-apps-cli@latest deploy $outputDir `
    --deployment-token $token `
    --env production
if ($LASTEXITCODE -ne 0) { throw "the upload failed with exit code $LASTEXITCODE" }

Write-Host '    uploaded'
