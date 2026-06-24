#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [int]$PilotUserId,
    [string]$ProductionSettingsPath = "C:\Uqeb\publish\api\appsettings.Production.json",
    [string]$BackupRoot = "C:\Uqeb\backup",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ProductionSettingsPath)) {
    throw "Production settings not found: $ProductionSettingsPath"
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $BackupRoot "before-phase1-$stamp"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$backupFile = Join-Path $backupDir "appsettings.Production.json"
Copy-Item $ProductionSettingsPath $backupFile -Force

$config = Get-Content $ProductionSettingsPath -Raw | ConvertFrom-Json

if (-not $config.FeatureFlags) { $config | Add-Member -NotePropertyName FeatureFlags -NotePropertyValue (@{}) }
if (-not $config.ReportingRollout) { $config | Add-Member -NotePropertyName ReportingRollout -NotePropertyValue (@{}) }

$config.FeatureFlags.InstitutionalReports = $true
$config.ReportingRollout.EmergencyDisable = $false
$config.ReportingRollout.EnabledForUserIds = @($PilotUserId)
$config.ReportingRollout.EnabledForRoles = @()
$config.ReportingRollout.EnabledForDepartments = @()
$config.ReportingRollout.Percentage = 0

$json = $config | ConvertTo-Json -Depth 20

Write-Host "Phase 1 allowlist patch prepared."
Write-Host "Backup: $backupFile"
Write-Host "Pilot user id: $PilotUserId"
Write-Host "EnabledForRoles: (empty)"
Write-Host "EnabledForDepartments: (empty)"
Write-Host "Percentage: 0"

if ($WhatIf) {
    Write-Host "WhatIf: settings file was NOT modified."
    Write-Host $json
    exit 0
}

$json | Set-Content $ProductionSettingsPath -Encoding UTF8
Write-Host "Updated $ProductionSettingsPath"
Write-Host "Restart UqebApi scheduled task after validation."
