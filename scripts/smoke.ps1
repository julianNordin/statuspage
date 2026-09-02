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
      the seeded operator can sign in, which means somebody can actually administer this
      writes are closed to anonymous callers, which means the fallback policy survived
      the snapshot is readable anonymously, which means the container access level is right
      the snapshot is readable cross-origin, which means the CORS rule is there
      the static site serves something
      the site's config.json is the one this deployment wrote, which means the page is
        pointed at this environment rather than at a developer's laptop

    The cross-origin check is the one that would otherwise be found by a reader rather than
    by us: without it the page loads, renders, and shows nothing but a failure message. The
    config.json check is there for the same reason and fails the same way — quietly.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApiUrl,
    [Parameter(Mandatory)] [string] $SnapshotUrl,
    [Parameter(Mandatory)] [string] $SiteUrl,
    [string] $OperatorEmail,
    [string] $OperatorPassword
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

# The check that would have caught the deployment coming up unusable. Operators are seeded
# from configuration and there is no registration endpoint, so an account that was not seeded
# cannot be created afterwards by any means the product offers. When the template passed no
# Operators section the API skipped seeding entirely, every other check here still passed, and
# what was deployed was a status page nobody could ever put a component into.
if ($OperatorEmail -and $OperatorPassword) {
    Test-Claim 'the seeded operator can sign in' {
        $response = Invoke-WebRequest -Uri "$ApiUrl/api/auth/token" -Method Post `
            -Body (@{ email = $OperatorEmail; password = $OperatorPassword } | ConvertTo-Json) `
            -ContentType 'application/json' -TimeoutSec 90 -SkipHttpErrorCheck
        if ($response.StatusCode -ne 200) {
            throw "status $($response.StatusCode); the operator this deployment configured cannot sign in, so nobody can administer it"
        }
        if (-not ($response.Content | ConvertFrom-Json).accessToken) {
            throw 'signed in but got no access token'
        }
    }
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

# Serving something is a weak claim, and on its own it passed against the placeholder page
# Azure serves before anything has been uploaded at all. What the page needs is config.json,
# and its absence is silent by design: the loader falls back to same-origin defaults rather
# than blanking the page, so a site with the wrong one renders a tidy failure message and
# still answers 200 here. The copy in client/public points at Azurite, which makes shipping
# the bundle unchanged the likeliest way to break this deployment.
Test-Claim 'the site is configured for this deployment' {
    $response = Invoke-WebRequest -Uri "$SiteUrl/config.json" -TimeoutSec 60 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) {
        throw "no config.json (status $($response.StatusCode)); the page will fall back to same-origin defaults and report nothing"
    }
    $config = $response.Content | ConvertFrom-Json
    if ($config.snapshotUrl -ne $SnapshotUrl) {
        throw "config.json points at $($config.snapshotUrl), not $SnapshotUrl"
    }
}

if ($script:failures -gt 0) {
    Write-Host ''
    Write-Host "$($script:failures) check(s) failed. The deployment is not good." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Environment verified.' -ForegroundColor Green
