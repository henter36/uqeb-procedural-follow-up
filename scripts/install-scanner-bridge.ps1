#Requires -Version 5.1
<#
.SYNOPSIS
  تثبيت وتشغيل Uqeb Scanner Bridge كخدمة Windows محلية.

.DESCRIPTION
  يُشغّل على جهاز Windows الذي يحتوي الماسح والمتصفح. الخدمة تستمع على
  http://127.0.0.1:5055 فقط ولا تُفتح على الشبكة.
#>

[CmdletBinding()]
param(
    [string]$PackagePath = "",
    [string]$SourcePath = "",
    [string]$InstallPath = "C:\Uqeb\scanner-bridge",
    [string]$ServiceName = "UqebScannerBridge",
    [string]$DisplayName = "Uqeb Scanner Bridge",
    [string]$AllowedOrigins = "http://localhost,http://127.0.0.1,http://[::1]"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Resolve-ScannerBridgeSource {
    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        if (-not (Test-Path -LiteralPath (Join-Path $SourcePath "Uqeb.ScannerBridge.exe"))) {
            throw "SourcePath لا يحتوي Uqeb.ScannerBridge.exe: $SourcePath"
        }
        return [pscustomobject]@{ Path = (Resolve-Path -LiteralPath $SourcePath).Path; TempRoot = "" }
    }

    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        throw "حدد SourcePath أو PackagePath."
    }
    if (-not (Test-Path -LiteralPath $PackagePath)) {
        throw "PackagePath غير موجود: $PackagePath"
    }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("uqeb-scanner-bridge-" + [Guid]::NewGuid().ToString("N"))
    try {
        Ensure-Directory $tempRoot
        Expand-Archive -LiteralPath $PackagePath -DestinationPath $tempRoot -Force
        $source = Join-Path $tempRoot "scanner-bridge"
        if (-not (Test-Path -LiteralPath (Join-Path $source "Uqeb.ScannerBridge.exe"))) {
            throw "الحزمة لا تحتوي scanner-bridge\Uqeb.ScannerBridge.exe"
        }

        return [pscustomobject]@{ Path = $source; TempRoot = $tempRoot }
    }
    catch {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
}

function ConvertTo-ScannerBridgeAllowedOrigins {
    param([string]$Origins)

    $result = New-Object System.Collections.Generic.List[string]
    foreach ($origin in ($Origins -split ',')) {
        $normalized = $origin.Trim().TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            continue
        }
        if ($normalized -eq '*') {
            throw "AllowedOrigins لا تقبل wildcard (*)."
        }
        $uri = $null
        if (-not [Uri]::TryCreate($normalized, [UriKind]::Absolute, [ref]$uri)) {
            throw "AllowedOrigins يحتوي origin غير صالح: $normalized"
        }
        if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') {
            throw "AllowedOrigins يقبل http/https فقط: $normalized"
        }
        if (-not $result.Contains($normalized)) {
            $result.Add($normalized)
        }
    }

    if ($result.Count -eq 0) {
        throw "AllowedOrigins لا يحتوي أي origin صالح."
    }

    return $result.ToArray()
}

function Set-ScannerBridgeCorsOrigins {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][string[]]$Origins
    )

    if (Test-Path -LiteralPath $SettingsPath) {
        $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    }
    else {
        $settings = [pscustomobject]@{}
    }

    if (-not (Get-Member -InputObject $settings -Name 'Cors' -MemberType NoteProperty)) {
        $settings | Add-Member -MemberType NoteProperty -Name 'Cors' -Value ([pscustomobject]@{})
    }
    if (Get-Member -InputObject $settings.Cors -Name 'AllowedOrigins' -MemberType NoteProperty) {
        $settings.Cors.AllowedOrigins = $Origins
    }
    else {
        $settings.Cors | Add-Member -MemberType NoteProperty -Name 'AllowedOrigins' -Value $Origins
    }

    $settings |
        ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $SettingsPath -Encoding UTF8
}

function Get-ScannerBridgeLogTail {
    param([string]$LogPath)

    if (Test-Path -LiteralPath $LogPath) {
        return Get-Content -LiteralPath $LogPath -Tail 50 -ErrorAction SilentlyContinue
    }

    return @()
}

function Wait-ScannerBridgeHealthy {
    param(
        [string]$StatusUrl = "http://127.0.0.1:5055/status",
        [int]$TimeoutSeconds = 45,
        [int]$RetryDelaySeconds = 1
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $status = Invoke-RestMethod -Uri $StatusUrl -TimeoutSec 5
            if ($status.ok -eq $true) {
                return $status
            }
        }
        catch {
            # Retry until the service is fully ready.
        }

        Start-Sleep -Seconds $RetryDelaySeconds
    }

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Warning ("Scanner Bridge service state: " + $service.Status)
    }

    $logTail = Get-ScannerBridgeLogTail -LogPath (Join-Path $InstallPath "logs\scanner-bridge.log")
    if ($logTail.Count -gt 0) {
        Write-Warning "Last Scanner Bridge log lines:"
        $logTail | ForEach-Object { Write-Warning $_ }
    }

    throw "Scanner Bridge service started but did not become healthy on $StatusUrl within $TimeoutSeconds seconds."
}

$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $isWindowsPlatform) {
    throw "Scanner Bridge يعمل على Windows فقط."
}

$sourceInfo = $null
$sourceInfo = Resolve-ScannerBridgeSource
try {
    Ensure-Directory $InstallPath

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force -ErrorAction Stop
            $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        }
    }

    robocopy $sourceInfo.Path $InstallPath /E /R:2 /W:2 | Out-Host
    if ($LASTEXITCODE -ge 8) {
        throw "فشل نسخ Scanner Bridge إلى $InstallPath. Robocopy exit code: $LASTEXITCODE"
    }

    $exePath = Join-Path $InstallPath "Uqeb.ScannerBridge.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Uqeb.ScannerBridge.exe غير موجود بعد النسخ: $exePath"
    }

    $normalizedAllowedOrigins = ConvertTo-ScannerBridgeAllowedOrigins -Origins $AllowedOrigins
    Set-ScannerBridgeCorsOrigins `
        -SettingsPath (Join-Path $InstallPath "appsettings.json") `
        -Origins $normalizedAllowedOrigins

    if (-not $existing) {
        New-Service `
            -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName $DisplayName `
            -Description "Local loopback scanner bridge for Uqeb attachments." `
            -StartupType Automatic | Out-Null
    }

    Start-Service -Name $ServiceName
    $status = Wait-ScannerBridgeHealthy

    Write-Host "Scanner Bridge installed and running."
    Write-Host "ServiceName: $ServiceName"
    Write-Host "InstallPath: $InstallPath"
    Write-Host ("AllowedOrigins: " + ($normalizedAllowedOrigins -join ', '))
    Write-Host "Status: http://127.0.0.1:5055/status"
}
finally {
    if ($null -ne $sourceInfo -and $sourceInfo.TempRoot -and (Test-Path -LiteralPath $sourceInfo.TempRoot)) {
        Remove-Item -LiteralPath $sourceInfo.TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
