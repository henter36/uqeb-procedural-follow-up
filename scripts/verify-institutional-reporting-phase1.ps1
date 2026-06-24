#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Development", "Test", "Staging", "Production")]
    [string]$EnvironmentName,

    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [string]$AdminUsername = "admin",

    [string]$NonPilotAdminUsername = "",

    [string]$NormalUsername = "",

    [string]$AdminPassword = $env:UQEB_PASSWORD,

    [string]$NonPilotPassword = $env:UQEB_NON_PILOT_PASSWORD,

    [string]$NormalPassword = $env:UQEB_NORMAL_PASSWORD,

    [switch]$TestEmergencyDisable,

    [string]$SettingsPath = "",

    [string]$ProjectRoot = ""
)

$ErrorActionPreference = "Stop"

function Invoke-UqebLogin {
    param([string]$Username, [string]$Password)
    $body = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Method Post `
            -Uri "$ApiBaseUrl/auth/login" -ContentType "application/json" -Body $body
        return ($response.Content | ConvertFrom-Json).token
    }
    catch {
        throw "Login failed for user '$Username'."
    }
}

function Test-InstitutionalEndpoint {
    param(
        [string]$Token,
        [string]$Path,
        [string]$Method = "Get",
        [object]$Body = $null,
        [switch]$ExpectFailure
    )

    $headers = @{ Authorization = "Bearer $Token" }
    $params = @{ Uri = "$ApiBaseUrl$Path"; Method = $Method; Headers = $headers; UseBasicParsing = $true }
    if ($Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }

    try {
        $response = Invoke-WebRequest @params
        if ($ExpectFailure) {
            throw "Expected denial for $Path but received $($response.StatusCode)."
        }
        return $response
    }
    catch [System.Net.WebException] {
        if (-not $ExpectFailure) { throw }
        return $_.Exception.Response
    }
}

function Assert-Denied {
    param($Response)
    $status = [int]$Response.StatusCode
    if ($status -ne 404 -and $status -ne 403) {
        throw "Expected 404 or 403 denial, got $status."
    }
}

$previewBody = @{
    reportType = 1
    sectionIds = @(1, 2)
    filters = @{ dateFrom = "2025-01-01"; dateTo = "2025-12-31" }
}

$pdfExportBody = @{
    reportType = 1
    sectionIds = @(1, 2)
    exportFormat = 1
    filters = @{ dateFrom = "2025-01-01"; dateTo = "2025-12-31" }
    detailOverflowAction = 0
}

$xlsxExportBody = @{
    reportType = 1
    sectionIds = @(1, 2)
    exportFormat = 3
    filters = @{ dateFrom = "2025-01-01"; dateTo = "2025-12-31" }
    detailOverflowAction = 0
}

Write-Host "Phase 1 verification"
Write-Host "Environment: $EnvironmentName"
Write-Host "API base URL: $ApiBaseUrl"
Write-Host "Pilot username: $AdminUsername"

if (-not $AdminPassword) {
    throw "Admin password missing. Set `$env:UQEB_PASSWORD."
}

$adminToken = Invoke-UqebLogin -Username $AdminUsername -Password $AdminPassword
Write-Host "Admin login: PASS"

$config = Test-InstitutionalEndpoint -Token $adminToken -Path "/institutional-reports/configuration"
$readiness = Test-InstitutionalEndpoint -Token $adminToken -Path "/institutional-reports/readiness"
$preview = Test-InstitutionalEndpoint -Token $adminToken -Path "/institutional-reports/preview" -Method Post -Body $previewBody
$pdf = Test-InstitutionalEndpoint -Token $adminToken -Path "/institutional-reports/export" -Method Post -Body $pdfExportBody
$xlsx = Test-InstitutionalEndpoint -Token $adminToken -Path "/institutional-reports/export" -Method Post -Body $xlsxExportBody

Write-Host "Configuration: $($config.StatusCode)"
Write-Host "Readiness: $($readiness.StatusCode)"
Write-Host "Preview: $($preview.StatusCode)"
Write-Host "PDF export: $($pdf.StatusCode)"
Write-Host "XLSX export: $($xlsx.StatusCode)"

$readinessBody = $readiness.Content | ConvertFrom-Json
Write-Host "Readiness state: $($readinessBody.state)"

if ($readinessBody.state -ne "Ready") {
    throw "Readiness is $($readinessBody.state); expected Ready."
}

if ($NonPilotAdminUsername -and $NonPilotPassword) {
    $otherToken = Invoke-UqebLogin -Username $NonPilotAdminUsername -Password $NonPilotPassword
    $denied = Test-InstitutionalEndpoint -Token $otherToken -Path "/institutional-reports/configuration" -ExpectFailure
    Assert-Denied -Response $denied
    Write-Host "Other admin denied: PASS"
}

if ($NormalUsername -and $NormalPassword) {
    $normalToken = Invoke-UqebLogin -Username $NormalUsername -Password $NormalPassword
    $denied = Test-InstitutionalEndpoint -Token $normalToken -Path "/institutional-reports/configuration" -ExpectFailure
    Assert-Denied -Response $denied
    Write-Host "Normal user denied: PASS"
}

if ($TestEmergencyDisable) {
    Write-Host "Emergency disable test requires manual toggle and re-run."
    Write-Host "Set ReportingRollout.EmergencyDisable=true, restart API, verify admin denied, then restore false."
}

Write-Host "Phase 1 verification for $EnvironmentName : PASS"
