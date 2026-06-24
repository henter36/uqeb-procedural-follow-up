#Requires -Version 5.1
<#
.SYNOPSIS
  تشخيص جاهزية Chromium/Playwright على خادم الإنتاج دون استدعاء تصدير رسمي.
#>

[CmdletBinding()]
param(
    [string]$PlaywrightBrowsersPath = "C:\Uqeb\tools\ms-playwright",
    [string]$BrowserManifestPath = "",
    [string]$ExpectedBrowserExecutableSha256 = "",
    [switch]$SkipProcessSmokeTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    $commonPath = Join-Path "C:\UqebTools" "deployment\Common.ps1"
}
. $commonPath

if ([string]::IsNullOrWhiteSpace($BrowserManifestPath)) {
    $BrowserManifestPath = Join-Path $PlaywrightBrowsersPath "playwright-browser-manifest.json"
}

Write-DeployStep "التحقق من مسار PLAYWRIGHT_BROWSERS_PATH"
if (-not (Test-Path -LiteralPath $PlaywrightBrowsersPath)) {
    throw "مجلد المتصفحات غير موجود: $PlaywrightBrowsersPath"
}

$manifest = $null
if (Test-Path -LiteralPath $BrowserManifestPath) {
    $manifest = Get-Content -LiteralPath $BrowserManifestPath -Raw | ConvertFrom-Json
}

$expectedRelative = if ($manifest) { [string]$manifest.executableRelativePath } else { "" }
$expectedHash = if ($ExpectedBrowserExecutableSha256) {
    $ExpectedBrowserExecutableSha256
}
elseif ($manifest) {
    [string]$manifest.browserExecutableSha256
}
else {
    ""
}

$executable = Test-PlaywrightBrowserPayload `
    -BrowsersRoot $PlaywrightBrowsersPath `
    -ExpectedExecutableRelativePath $expectedRelative `
    -ExpectedExecutableSha256 $expectedHash

Write-DeployInfo ("Chromium executable: " + $executable.FullPath)
Write-DeployInfo ("Chromium size (bytes): " + $executable.SizeBytes)

if (-not $SkipProcessSmokeTest) {
    Write-DeployStep "تشغيل Chromium headless ثم إغلاقه"
    $process = Start-Process -FilePath $executable.FullPath `
        -ArgumentList @('--headless', '--disable-gpu', '--no-sandbox', '--dump-dom', 'about:blank') `
        -PassThru `
        -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(15000)) {
            throw "انتهت مهلة تشغيل Chromium أثناء smoke test."
        }
        if ($process.ExitCode -ne 0) {
            throw ("Chromium smoke test exited with code " + $process.ExitCode)
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-DeployInfo "Playwright readiness verification passed."
exit 0
