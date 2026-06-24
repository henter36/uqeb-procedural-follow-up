#Requires -Version 5.1
<#
.SYNOPSIS
  Provisions Uqeb database explicitly outside API startup.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SettingsPath,

    [switch]$ApplyMigrations,
    [switch]$CreateReferenceData,
    [switch]$CreateDefaultUsers,
    [switch]$CreateDemoData,

    [string]$ExpectedDatabaseName,
    [string]$BackupPath,
    [string]$BackupSha256,
    [string]$ConfirmationToken,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "backend/Uqeb.Tools.DatabaseProvision/Uqeb.Tools.DatabaseProvision.csproj"

if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "Database provision tool project was not found: $toolProject"
}

if (-not $ApplyMigrations -and -not $CreateReferenceData -and -not $CreateDefaultUsers -and -not $CreateDemoData) {
    throw "Select at least one provisioning action."
}

$arguments = @(
    "run",
    "--project", $toolProject,
    "-c", "Release",
    "--",
    "--settings-path", (Resolve-Path -LiteralPath $SettingsPath).Path
)

if ($ApplyMigrations) { $arguments += "--apply-migrations" }
if ($CreateReferenceData) { $arguments += "--create-reference-data" }
if ($CreateDefaultUsers) { $arguments += "--create-default-users" }
if ($CreateDemoData) { $arguments += "--create-demo-data" }
if ($ExpectedDatabaseName) { $arguments += @("--expected-database-name", $ExpectedDatabaseName) }
if ($BackupPath) { $arguments += @("--backup-path", (Resolve-Path -LiteralPath $BackupPath).Path) }
if ($BackupSha256) { $arguments += @("--backup-sha256", $BackupSha256) }
if ($ConfirmationToken) { $arguments += @("--confirmation-token", $ConfirmationToken) }
if ($VerboseOutput) { $arguments += "--verbose" }

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Database provisioning failed with exit code $LASTEXITCODE."
}

Write-Host "Database provisioning completed successfully."
