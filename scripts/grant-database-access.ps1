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

    Written to be run twice. Every statement checks first, so a redeploy is a no-op rather
    than an error.
#>
[CmdletBinding()]
param(
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

Write-Host "    granting $IdentityName on $Database"

# An access token for the SQL resource, from the principal already signed in. No password is
# typed, stored, or passed on a command line.
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv

if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    Write-Host '    installing the SqlServer module (first run only)'
    Install-Module SqlServer -Scope CurrentUser -Force -AllowClobber
}

Import-Module SqlServer

# QUOTED_IDENTIFIER matters here. This database has a filtered index and SQL Server refuses
# DML on such a table without it; Invoke-Sqlcmd defaults it off, exactly as sqlcmd does.
Invoke-Sqlcmd `
    -ServerInstance $ServerFqdn `
    -Database $Database `
    -AccessToken $token `
    -Query $sql `
    -QueryTimeout 60 `
    -ErrorAction Stop

Write-Host '    granted'
