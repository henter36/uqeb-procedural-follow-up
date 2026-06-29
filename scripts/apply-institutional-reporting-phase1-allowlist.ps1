#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Staging", "Production")]
    [string]$EnvironmentName,

    [string]$AdminUsername = "admin",

    [string]$SettingsPath = "",

    [string]$ProjectRoot = "",

    [string]$BackupRoot = "",

    [switch]$WhatIf,

    [switch]$Enforce
)

$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    $current = $PSScriptRoot
    while ($current) {
        if (Test-Path (Join-Path $current ".git")) {
            return $current
        }
        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }
    throw "Unable to locate repository root."
}

function Get-DefaultSettingsPath {
    param([string]$Environment, [string]$Root)
    switch ($Environment) {
        "Development" { return Join-Path $Root "backend/Uqeb.Api/appsettings.Development.json" }
        "Test" { return Join-Path $Root "backend/Uqeb.Api/appsettings.Test.json" }
        "Staging" { return Join-Path $Root "backend/Uqeb.Api/appsettings.Staging.json" }
        "Production" { return "C:\Uqeb\publish\api\appsettings.Production.json" }
        default { throw "Unsupported environment: $Environment" }
    }
}

function Get-DefaultBackupRoot {
    param([string]$Environment)
    if ($Environment -eq "Production") {
        return "C:\Uqeb\backup"
    }
    return Join-Path (Get-RepositoryRoot) "artifacts/phase1-settings-backups"
}

function Get-MaskedUserId {
    param([int]$UserId)
    $text = "$UserId"
    if ($text.Length -le 4) {
        return ("*" * $text.Length)
    }
    return "{0}…{1}" -f $text.Substring(0, 4), $text.Substring($text.Length - 4)
}

function Read-RolloutSnapshot {
    param($Config)
    $enforcementMode = if ($Config.ReportingRollout.EnforcementMode) {
        [string]$Config.ReportingRollout.EnforcementMode
    }
    else {
        "ObserveOnly"
    }

    return [ordered]@{
        FeatureFlag = [bool]$Config.FeatureFlags.InstitutionalReports
        EnforcementMode = $enforcementMode
        EmergencyDisable = [bool]$Config.ReportingRollout.EmergencyDisable
        EnabledForUserIds = @($Config.ReportingRollout.EnabledForUserIds)
        EnabledForRoles = @($Config.ReportingRollout.EnabledForRoles)
        EnabledForDepartments = @($Config.ReportingRollout.EnabledForDepartments)
        Percentage = [int]$Config.ReportingRollout.Percentage
    }
}

$repoRoot = if ($ProjectRoot) { $ProjectRoot } else { Get-RepositoryRoot }
$resolvedSettingsPath = if ($SettingsPath) { $SettingsPath } else { Get-DefaultSettingsPath -Environment $EnvironmentName -Root $repoRoot }
$resolvedBackupRoot = if ($BackupRoot) { $BackupRoot } else { Get-DefaultBackupRoot -Environment $EnvironmentName }

if (-not (Test-Path $resolvedSettingsPath)) {
    throw "Settings file not found for $EnvironmentName`: $resolvedSettingsPath"
}

$toolProject = Join-Path $repoRoot "backend/Uqeb.Tools.Phase1Rollout/Uqeb.Tools.Phase1Rollout.csproj"
if (-not (Test-Path $toolProject)) {
    throw "Phase 1 rollout tool not found: $toolProject"
}

$targetEnforcementMode = if ($Enforce) { "Enforced" } else { "ObserveOnly" }

Write-Host "Environment: $EnvironmentName"
Write-Host "Settings path: $resolvedSettingsPath"
Write-Host "Admin username lookup: $AdminUsername"
Write-Host "Current mode: (read from settings after load)"
Write-Host "Target mode: $targetEnforcementMode"

$resolveOutput = & dotnet run --project $toolProject -- `
    resolve-admin `
    --settings-path $resolvedSettingsPath `
    --admin-username $AdminUsername 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Admin username resolution failed for $EnvironmentName. Settings were not modified."
    Write-Host $resolveOutput
    exit 1
}

$resolution = $resolveOutput | Select-Object -Last 1 | ConvertFrom-Json
if ($resolution.status -ne "Success" -or -not $resolution.userId) {
    Write-Error "Admin username resolution failed: $($resolution.status). Settings were not modified."
    exit 1
}

$adminUserId = [int]$resolution.userId
$maskedUserId = Get-MaskedUserId -UserId $adminUserId

$config = Get-Content $resolvedSettingsPath -Raw | ConvertFrom-Json
$currentSnapshot = Read-RolloutSnapshot -Config $config

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupDir = Join-Path $resolvedBackupRoot "$EnvironmentName-before-phase1-$stamp"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$backupFile = Join-Path $backupDir (Split-Path $resolvedSettingsPath -Leaf)

if (-not $config.FeatureFlags) {
    $config | Add-Member -NotePropertyName FeatureFlags -NotePropertyValue ([pscustomobject]@{
        InstitutionalReports = $false
    })
}
if (-not $config.ReportingRollout) {
    $config | Add-Member -NotePropertyName ReportingRollout -NotePropertyValue ([pscustomobject]@{
        EnforcementMode = "ObserveOnly"
        EmergencyDisable = $false
        EnabledForUserIds = @()
        EnabledForRoles = @()
        EnabledForDepartments = @()
        Percentage = 0
    })
}

Write-Host "Resolved admin user ID (masked): $maskedUserId"
Write-Host "Current mode: $($currentSnapshot.EnforcementMode)"
Write-Host "Target mode: $targetEnforcementMode"
Write-Host "Current feature flag: $($currentSnapshot.FeatureFlag)"
Write-Host "Current rollout configuration:"
Write-Host "  EnforcementMode=$($currentSnapshot.EnforcementMode)"
Write-Host "  EmergencyDisable=$($currentSnapshot.EmergencyDisable)"
Write-Host "  EnabledForUserIds count=$($currentSnapshot.EnabledForUserIds.Count)"
Write-Host "  EnabledForRoles count=$($currentSnapshot.EnabledForRoles.Count)"
Write-Host "  EnabledForDepartments count=$($currentSnapshot.EnabledForDepartments.Count)"
Write-Host "  Percentage=$($currentSnapshot.Percentage)"
Write-Host "Backup path: $backupFile"
Write-Host "Planned configuration changes:"
Write-Host "  FeatureFlags.InstitutionalReports=true"
Write-Host "  ReportingRollout.EnforcementMode=$targetEnforcementMode"
Write-Host "  ReportingRollout.EmergencyDisable=false"
Write-Host "  ReportingRollout.EnabledForUserIds=[$maskedUserId]"
Write-Host "  ReportingRollout.EnabledForRoles=[]"
Write-Host "  ReportingRollout.EnabledForDepartments=[]"
Write-Host "  ReportingRollout.Percentage=0"
Write-Host "Required service restart: yes (restart API for $EnvironmentName)"

if (-not $Enforce) {
    Write-Host "ObserveOnly: rollout decisions are recorded but not enforced. All normally authorized users retain access."
}

if ($WhatIf) {
    Write-Host "WhatIf: settings file was NOT modified."
    exit 0
}

Copy-Item $resolvedSettingsPath $backupFile -Force

$config.FeatureFlags.InstitutionalReports = $true
$config.ReportingRollout | Add-Member -NotePropertyName EnforcementMode -NotePropertyValue $targetEnforcementMode -Force
$config.ReportingRollout.EnforcementMode = $targetEnforcementMode
$config.ReportingRollout.EmergencyDisable = $false
$config.ReportingRollout.EnabledForUserIds = @($adminUserId)
$config.ReportingRollout.EnabledForRoles = @()
$config.ReportingRollout.EnabledForDepartments = @()
$config.ReportingRollout.Percentage = 0

($config | ConvertTo-Json -Depth 20) | Set-Content $resolvedSettingsPath -Encoding UTF8

Write-Host "Updated $resolvedSettingsPath for $EnvironmentName."
Write-Host "Restart the API service, then run verify-institutional-reporting-phase1.ps1."
