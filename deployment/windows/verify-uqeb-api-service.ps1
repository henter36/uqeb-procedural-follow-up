#Requires -Version 5.1
<#
.SYNOPSIS
  Verifies the UqebApi Windows Service is installed, running, listening, and
  healthy. Prints a PASS/FAIL line per check plus an overall summary, and
  exits 0 only if every non-skipped check passed.

.PARAMETER ServiceName
  Windows Service name. Default: UqebApi.

.PARAMETER ExpectedBinaryPath
  Expected Uqeb.Api.exe path for the running process. Default:
  C:\Uqeb\current\api\Uqeb.Api.exe.

.PARAMETER ApiPort
  Port the API should be listening on. Default: 5000.

.PARAMETER ApiBindAddress
  The address the service is actually configured to bind to (same meaning as
  install-uqeb-api-service.ps1's -ApiBindAddress). Used to resolve the correct
  host for the primary health/live and health/ready checks: empty/0.0.0.0/*/+
  resolve to "localhost"; a specific IP (e.g. 10.0.177.17) is probed directly,
  since a service bound to one specific IP does not also listen on loopback.
  When set to a specific IP, it is also probed as an additional
  reachability check (HealthLive_NetworkIp), reported as SKIP rather than
  FAIL if that address isn't reachable from this host.

.PARAMETER LogPath
  Legacy runtime log file to check for size/recent errors, if it still exists.
  Default: C:\Uqeb\logs\api-runtime.log.

.PARAMETER MaxLogSizeMB
  Log size threshold. Default: 100.

.EXAMPLE
  .\verify-uqeb-api-service.ps1
.EXAMPLE
  .\verify-uqeb-api-service.ps1 -ApiBindAddress 10.0.177.17
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "UqebApi",
    [string]$ExpectedBinaryPath = "C:\Uqeb\current\api\Uqeb.Api.exe",
    [int]$ApiPort = 5000,
    [string]$ApiBindAddress = "",
    [string]$LogPath = "C:\Uqeb\logs\api-runtime.log",
    [int]$MaxLogSizeMB = 100,
    [int]$RecentLogTailLines = 100,
    [int]$HealthTimeoutSec = 10
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "UqebServiceCommon.ps1")

$script:results = New-Object System.Collections.Generic.List[pscustomobject]

function Add-CheckResult {
    param(
        [string]$Name,
        [ValidateSet('PASS', 'FAIL', 'SKIP')]
        [string]$Status,
        [string]$Detail = ""
    )

    $script:results.Add([pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail })
}

function Test-UrlReturns200 {
    param([string]$Uri, [int]$TimeoutSec)
    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec $TimeoutSec
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

# 1. Service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    Add-CheckResult -Name "ServiceExists" -Status 'PASS' -Detail "Service '$ServiceName' found."
}
else {
    Add-CheckResult -Name "ServiceExists" -Status 'FAIL' -Detail "Service '$ServiceName' not found."
}

# 2. Service running
if ($service -and $service.Status -eq 'Running') {
    Add-CheckResult -Name "ServiceRunning" -Status 'PASS' -Detail "Status: Running."
}
elseif ($service) {
    Add-CheckResult -Name "ServiceRunning" -Status 'FAIL' -Detail "Status: $($service.Status)."
}
else {
    Add-CheckResult -Name "ServiceRunning" -Status 'FAIL' -Detail "Service not found."
}

# 3. Process path matches expected binary
if ($service -and $service.Status -eq 'Running') {
    try {
        $wmiService = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
        $processId = $wmiService.ProcessId
        $process = Get-Process -Id $processId -ErrorAction Stop
        $actualPath = $process.Path
        if (-not $actualPath) {
            Add-CheckResult -Name "ProcessPathMatches" -Status 'FAIL' -Detail "Process (PID $processId) has no accessible Path."
        }
        else {
            # Both sides are already-absolute paths (a running process's .Path,
            # and -ExpectedBinaryPath), so GetFullPath here only normalizes
            # slash direction/redundant separators/case-of-drive-letter — it
            # does not depend on the current working directory.
            $normalizedActual = [System.IO.Path]::GetFullPath($actualPath).TrimEnd('\')
            $normalizedExpected = [System.IO.Path]::GetFullPath($ExpectedBinaryPath).TrimEnd('\')
            if ($normalizedActual -ieq $normalizedExpected) {
                Add-CheckResult -Name "ProcessPathMatches" -Status 'PASS' -Detail "PID $processId -> $actualPath"
            }
            else {
                Add-CheckResult -Name "ProcessPathMatches" -Status 'FAIL' -Detail "Expected '$ExpectedBinaryPath' but process path is '$actualPath' (PID $processId)."
            }
        }
    }
    catch {
        Add-CheckResult -Name "ProcessPathMatches" -Status 'FAIL' -Detail "Could not resolve process for service: $($_.Exception.Message)"
    }
}
else {
    Add-CheckResult -Name "ProcessPathMatches" -Status 'SKIP' -Detail "Service is not running."
}

# 4. Port listening
try {
    $listener = Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue
    if ($listener) {
        Add-CheckResult -Name "PortListening" -Status 'PASS' -Detail "Port $ApiPort has an active listener."
    }
    else {
        Add-CheckResult -Name "PortListening" -Status 'FAIL' -Detail "No listener found on port $ApiPort."
    }
}
catch {
    Add-CheckResult -Name "PortListening" -Status 'FAIL' -Detail "Could not query port $ApiPort`: $($_.Exception.Message)"
}

# 5. health/live — resolved against the actual bind address, not a hardcoded
# "localhost": if the service is bound to one specific IP (not a wildcard),
# Kestrel does not also listen on loopback, so "localhost" would false-negative
# an otherwise-healthy service.
$healthHost = Get-UqebHealthHost -ApiBindAddress $ApiBindAddress
$localLiveUri = "http://${healthHost}:$ApiPort/health/live"
if (Test-UrlReturns200 -Uri $localLiveUri -TimeoutSec $HealthTimeoutSec) {
    Add-CheckResult -Name "HealthLive" -Status 'PASS' -Detail "$localLiveUri returned 200."
}
else {
    Add-CheckResult -Name "HealthLive" -Status 'FAIL' -Detail "$localLiveUri did not return 200."
}

# 6. health/ready — same resolved host as check 5.
$localReadyUri = "http://${healthHost}:$ApiPort/health/ready"
if (Test-UrlReturns200 -Uri $localReadyUri -TimeoutSec $HealthTimeoutSec) {
    Add-CheckResult -Name "HealthReady" -Status 'PASS' -Detail "$localReadyUri returned 200."
}
else {
    Add-CheckResult -Name "HealthReady" -Status 'FAIL' -Detail "$localReadyUri did not return 200."
}

# 7. health/live on public/network IP (optional, skip if unreachable)
if ([string]::IsNullOrWhiteSpace($ApiBindAddress)) {
    Add-CheckResult -Name "HealthLive_NetworkIp" -Status 'SKIP' -Detail "No -ApiBindAddress supplied."
}
else {
    $pingReachable = Test-Connection -ComputerName $ApiBindAddress -Count 1 -Quiet -ErrorAction SilentlyContinue
    if (-not $pingReachable) {
        Add-CheckResult -Name "HealthLive_NetworkIp" -Status 'SKIP' -Detail "$ApiBindAddress is not reachable from this host."
    }
    else {
        $networkLiveUri = "http://${ApiBindAddress}:$ApiPort/health/live"
        if (Test-UrlReturns200 -Uri $networkLiveUri -TimeoutSec $HealthTimeoutSec) {
            Add-CheckResult -Name "HealthLive_NetworkIp" -Status 'PASS' -Detail "$networkLiveUri returned 200."
        }
        else {
            Add-CheckResult -Name "HealthLive_NetworkIp" -Status 'FAIL' -Detail "$networkLiveUri did not return 200."
        }
    }
}

# 8. Recent log errors
if (-not (Test-Path -LiteralPath $LogPath)) {
    Add-CheckResult -Name "RecentLogErrors" -Status 'SKIP' -Detail "$LogPath does not exist (expected once the Scheduled Task's stdout redirection is retired)."
}
else {
    $tail = Get-Content -LiteralPath $LogPath -Tail $RecentLogTailLines -ErrorAction SilentlyContinue
    $errorLines = @($tail | Where-Object { $_ -match '(?i)\b(critical|error|unhandled exception)\b' })
    if ($errorLines.Count -eq 0) {
        Add-CheckResult -Name "RecentLogErrors" -Status 'PASS' -Detail "No Critical/Error lines in the last $RecentLogTailLines lines."
    }
    else {
        Add-CheckResult -Name "RecentLogErrors" -Status 'FAIL' -Detail "$($errorLines.Count) Critical/Error line(s) found in the last $RecentLogTailLines lines. First: $($errorLines[0])"
    }
}

# 9. Log file size
if (-not (Test-Path -LiteralPath $LogPath)) {
    Add-CheckResult -Name "LogFileSize" -Status 'PASS' -Detail "$LogPath does not exist; no bloat risk."
}
else {
    $sizeMB = [math]::Round((Get-Item -LiteralPath $LogPath).Length / 1MB, 1)
    if ($sizeMB -le $MaxLogSizeMB) {
        Add-CheckResult -Name "LogFileSize" -Status 'PASS' -Detail "$sizeMB MB (limit $MaxLogSizeMB MB)."
    }
    else {
        Add-CheckResult -Name "LogFileSize" -Status 'FAIL' -Detail "$sizeMB MB exceeds limit of $MaxLogSizeMB MB. Run rotate-uqeb-api-log.ps1."
    }
}

Write-Host ""
Write-Host "UqebApi service verification" -ForegroundColor Cyan
Write-Host "-----------------------------"
foreach ($result in $script:results) {
    $color = switch ($result.Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        default { 'Yellow' }
    }
    Write-Host ("[{0}] {1} - {2}" -f $result.Status, $result.Name, $result.Detail) -ForegroundColor $color
}

$failed = @($script:results | Where-Object { $_.Status -eq 'FAIL' })
Write-Host ""
if ($failed.Count -eq 0) {
    Write-Host "OVERALL: PASS" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "OVERALL: FAIL ($($failed.Count) check(s) failed)" -ForegroundColor Red
    exit 1
}
