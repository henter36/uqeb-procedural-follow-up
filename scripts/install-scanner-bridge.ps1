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
    [string]$DisplayName = "Uqeb Scanner Bridge"
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
    Ensure-Directory $tempRoot
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $tempRoot -Force
    $source = Join-Path $tempRoot "scanner-bridge"
    if (-not (Test-Path -LiteralPath (Join-Path $source "Uqeb.ScannerBridge.exe"))) {
        throw "الحزمة لا تحتوي scanner-bridge\Uqeb.ScannerBridge.exe"
    }

    return [pscustomobject]@{ Path = $source; TempRoot = $tempRoot }
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

    if (-not $existing) {
        New-Service `
            -Name $ServiceName `
            -BinaryPathName "`"$exePath`"" `
            -DisplayName $DisplayName `
            -Description "Local loopback scanner bridge for Uqeb attachments." `
            -StartupType Automatic | Out-Null
    }

    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $status = Invoke-RestMethod -Uri "http://127.0.0.1:5055/status" -TimeoutSec 10
    if (-not $status.ok) {
        throw "Scanner Bridge بدأ لكن /status لم يرجع ok=true."
    }

    Write-Host "Scanner Bridge installed and running."
    Write-Host "ServiceName: $ServiceName"
    Write-Host "InstallPath: $InstallPath"
    Write-Host "Status: http://127.0.0.1:5055/status"
}
finally {
    if ($null -ne $sourceInfo -and $sourceInfo.TempRoot -and (Test-Path -LiteralPath $sourceInfo.TempRoot)) {
        Remove-Item -LiteralPath $sourceInfo.TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
