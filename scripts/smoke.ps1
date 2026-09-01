#Requires -Version 7
<#
.SYNOPSIS
    Asserts the deployed environment actually works, and fails the deployment when it does not.

.DESCRIPTION
    A deploy that reports success without checking anything is a deploy nobody should trust.

    These are the claims worth checking after every deployment. Each is something that has
    been wrong at some point in this project, on a deployment that otherwise looked fine:

      the API answers at all
      the API answers /api/status, which means it reached the database
      writes are closed to anonymous callers, which means the fallback policy survived
      the snapshot is readable anonymously, which means the container access level is right
      the snapshot is readable cross-origin, which means the CORS rule is there
      the static site serves something

    The cross-origin check is the one that would otherwise be found by a reader rather than
    by us: without it the page loads, renders, and shows nothing but a failure message.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApiUrl,
    [Parameter(Mandatory)] [string] $SnapshotUrl,
    [Parameter(Mandatory)] [string] $SiteUrl
)

$ErrorActionPreference = 'Stop'
$script:failures = 0

function Test-Claim {
    param([string] $Name, [scriptblock] $Check)
    try {
        & $Check
        Write-Host "  ok    $Name" -ForegroundColor Green
    }
    catch {
        Write-Host "  FAIL  $Name - $($_.Exception.Message)" -ForegroundColor Red
        $script:failures++
    }
}

# The API scales to zero, so the first request pays a cold start on top of the database
# resuming from auto-pause. Ninety seconds is generous on purpose: a smoke test that fails on
# a cold start teaches everybody to ignore it.
Test-Claim 'the API answers /health' {
    $response = Invoke-WebRequest -Uri "$ApiUrl/health" -TimeoutSec 90 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) { throw "status $($response.StatusCode)" }
}

Test-Claim 'the API reaches the database' {
    $response = Invoke-WebRequest -Uri "$ApiUrl/api/status" -TimeoutSec 90 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) { throw "status $($response.StatusCode)" }
    $body = $response.Content | ConvertFrom-Json
    if ($null -eq $body.overall) { throw 'no overall state in the response' }
}

Test-Claim 'writes are closed to anonymous callers' {
    $response = Invoke-WebRequest -Uri "$ApiUrl/api/components" -Method Post `
        -Body '{}' -ContentType 'application/json' -TimeoutSec 60 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 401) { throw "expected 401, got $($response.StatusCode)" }
}

Test-Claim 'the snapshot is readable anonymously' {
    $response = Invoke-WebRequest -Uri $SnapshotUrl -TimeoutSec 60 -SkipHttpErrorCheck
    # 404 is fine before the checker has run for the first time. 403 is not, and means the
    # container public access level is wrong.
    if ($response.StatusCode -notin @(200, 404)) { throw "status $($response.StatusCode)" }
}

Test-Claim 'the snapshot allows a cross-origin read' {
    $response = Invoke-WebRequest -Uri $SnapshotUrl -Method Options -TimeoutSec 60 -SkipHttpErrorCheck `
        -Headers @{
            'Origin'                        = $SiteUrl
            'Access-Control-Request-Method' = 'GET'
        }
    if (-not $response.Headers['Access-Control-Allow-Origin']) {
        throw 'no Access-Control-Allow-Origin; the page cannot read this'
    }
}

Test-Claim 'the static site serves something' {
    $response = Invoke-WebRequest -Uri $SiteUrl -TimeoutSec 60 -SkipHttpErrorCheck
    if ($response.StatusCode -ge 500) { throw "status $($response.StatusCode)" }
}

if ($script:failures -gt 0) {
    Write-Host ''
    Write-Host "$($script:failures) check(s) failed. The deployment is not good." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Environment verified.' -ForegroundColor Green
