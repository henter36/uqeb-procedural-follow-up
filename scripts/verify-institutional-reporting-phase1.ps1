#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$PilotUsername,
    [string]$NonPilotAdminUsername = "",
    [string]$NormalUsername = "",
    [string]$PilotPassword = $env:UQEB_PASSWORD,
    [string]$NonPilotPassword = $env:UQEB_NON_PILOT_PASSWORD,
    [string]$NormalPassword = $env:UQEB_NORMAL_PASSWORD
)

$ErrorActionPreference = "Stop"

function Invoke-UqebLogin {
    param([string]$Username, [string]$Password)
    $body = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Method Post `
        -Uri "$ApiBaseUrl/auth/login" -ContentType "application/json" -Body $body
    return ($response.Content | ConvertFrom-Json).token
}

function Test-InstitutionalEndpoint {
    param([string]$Token, [string]$Path, [string]$Method = "Get", [object]$Body = $null)
    $headers = @{ Authorization = "Bearer $Token" }
    $params = @{ Uri = "$ApiBaseUrl$Path"; Method = $Method; Headers = $headers; UseBasicParsing = $true }
    if ($Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }
    return Invoke-WebRequest @params
}

Write-Host "Phase 1 verification against $ApiBaseUrl"

if (-not $PilotPassword) { throw "Pilot password missing. Set `$env:UQEB_PASSWORD." }

$pilotToken = Invoke-UqebLogin -Username $PilotUsername -Password $PilotPassword
$pilotConfig = Test-InstitutionalEndpoint -Token $pilotToken -Path "/institutional-reports/configuration"
$pilotReadiness = Test-InstitutionalEndpoint -Token $pilotToken -Path "/institutional-reports/readiness"
$pilotPreview = Test-InstitutionalEndpoint -Token $pilotToken -Path "/institutional-reports/preview" -Method Post -Body @{
    reportType = 1
    sectionIds = @(1, 2)
    filters = @{ dateFrom = "2025-01-01"; dateTo = "2025-12-31" }
}

Write-Host "Pilot configuration: $($pilotConfig.StatusCode)"
Write-Host "Pilot readiness: $($pilotReadiness.StatusCode)"
Write-Host "Pilot preview: $($pilotPreview.StatusCode)"

$readiness = $pilotReadiness.Content | ConvertFrom-Json
Write-Host "Readiness state: $($readiness.state)"

if ($NonPilotAdminUsername -and $NonPilotPassword) {
    $otherToken = Invoke-UqebLogin -Username $NonPilotAdminUsername -Password $NonPilotPassword
    try {
        Test-InstitutionalEndpoint -Token $otherToken -Path "/institutional-reports/configuration" | Out-Null
        throw "Non-pilot admin unexpectedly received configuration access."
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
        Write-Host "Non-pilot admin denied (404): PASS"
    }
}

if ($NormalUsername -and $NormalPassword) {
    $normalToken = Invoke-UqebLogin -Username $NormalUsername -Password $NormalPassword
    try {
        Test-InstitutionalEndpoint -Token $normalToken -Path "/institutional-reports/configuration" | Out-Null
        throw "Normal user unexpectedly received configuration access."
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }
        Write-Host "Normal user denied (404): PASS"
    }
}

if ($readiness.state -ne "Ready") {
    throw "Readiness is $($readiness.state); expected Ready."
}

Write-Host "Phase 1 verification: PASS"
