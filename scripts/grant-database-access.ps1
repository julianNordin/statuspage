#Requires -Version 7
<#
.SYNOPSIS
    Gives the workload's managed identity a database user.

.DESCRIPTION
    The SQL server is Entra-only: there is no SQL login and no password anywhere in this
    deployment. That is the point, and it has one consequence a template cannot handle.

    A managed identity can authenticate to the server and still has no user inside the
    database until somebody creates one. Creating it needs a connection as the Entra admin,
    which is a person rather than a resource, so this cannot live in Bicep and has to be a
    step the deploy script runs.

    That in turn needs the workstation to be able to reach the server, and the template's only
    firewall rule is AllowAllWindowsAzureIps — which lets Azure services in and a laptop
    nowhere. So this opens a rule for the current public address, does its work, and closes it
    again in a finally. The rule exists only while a human is using it, which is the only time
    it should.

    Written to be run twice. Every statement checks first, so a redeploy is a no-op rather
    than an error.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $ServerName,
    [Parameter(Mandatory)] [string] $ServerFqdn,
    [Parameter(Mandatory)] [string] $Database,
    [Parameter(Mandatory)] [string] $IdentityName
)

$ErrorActionPreference = 'Stop'

# db_ddladmin because the migration bundle creates tables; datareader and datawriter because
# the app reads and writes rows. Not db_owner: nothing here needs to alter security or drop
# the database, and an identity that could is one an SSRF bug could be aimed at.
$sql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$IdentityName')
BEGIN
    CREATE USER [$IdentityName] FROM EXTERNAL PROVIDER;
END;
IF IS_ROLEMEMBER('db_datareader', N'$IdentityName') = 0
    ALTER ROLE db_datareader ADD MEMBER [$IdentityName];
IF IS_ROLEMEMBER('db_datawriter', N'$IdentityName') = 0
    ALTER ROLE db_datawriter ADD MEMBER [$IdentityName];
IF IS_ROLEMEMBER('db_ddladmin', N'$IdentityName') = 0
    ALTER ROLE db_ddladmin ADD MEMBER [$IdentityName];
"@

$ruleName = 'deploy-workstation-temporary'
$myIp = (Invoke-RestMethod -Uri 'https://api.ipify.org?format=json' -TimeoutSec 30).ip
Write-Host "    opening the firewall for $myIp"

az sql server firewall-rule create `
    --resource-group $ResourceGroup `
    --server $ServerName `
    --name $ruleName `
    --start-ip-address $myIp `
    --end-ip-address $myIp `
    --output none

try {
    Write-Host "    granting $IdentityName on $Database"

    # An access token for the SQL resource, from the principal already signed in. No password
    # is typed, stored, or passed on a command line.
    $token = az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv

    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host '    installing the SqlServer module (first run only)'
        Install-Module SqlServer -Scope CurrentUser -Force -AllowClobber
    }

    Import-Module SqlServer

    # A firewall rule takes a moment to reach every gateway, and the first attempt after
    # creating one often lands on a node that has not seen it. Retrying beats telling somebody
    # to run the script again.
    $attempt = 0
    while ($true) {
        $attempt++
        try {
            Invoke-Sqlcmd `
                -ServerInstance $ServerFqdn `
                -Database $Database `
                -AccessToken $token `
                -Query $sql `
                -QueryTimeout 60 `
                -ErrorAction Stop
            break
        }
        catch {
            if ($attempt -ge 6) { throw }
            Write-Host "    not through yet, retrying ($attempt)"
            Start-Sleep -Seconds 15
        }
    }

    Write-Host '    granted'
}
finally {
    # Always, including on failure. A firewall rule left behind after a deploy is an opening
    # nobody remembers making.
    Write-Host '    closing the firewall'
    az sql server firewall-rule delete `
        --resource-group $ResourceGroup `
        --server $ServerName `
        --name $ruleName `
        --output none 2>$null
}
