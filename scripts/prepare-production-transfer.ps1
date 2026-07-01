#Requires -Version 5.1
<#
.SYNOPSIS
  يجهّز مجلد نقل كامل من آخر حزمة مبنية جاهزة للنسخ إلى جهاز الإنتاج.

.DESCRIPTION
  يعمل على جهاز البناء. لا يحتاج صلاحيات إنتاج.
  يختار آخر ZIP من artifacts\production، يختار SHA256 المطابق،
  ينسخ سكربتات النشر، وينشئ مجلد نقل في artifacts\transfer\.

.EXAMPLE
  pwsh -File .\scripts\prepare-production-transfer.ps1
#>

[CmdletBinding()]
param(
    [string]$ArtifactsRoot = "",
    [string]$TransferRoot = "",
    [string]$ZipPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot   = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repoRoot "artifacts"
}
if ([string]::IsNullOrWhiteSpace($TransferRoot)) {
    $TransferRoot = Join-Path $ArtifactsRoot "transfer"
}

$commonPath = Join-Path $scriptDir "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    throw "تعذر العثور على deployment\Common.ps1"
}
. $commonPath

Write-DeployStep "تحديد حزمة الإنتاج"

if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "الملف المحدد غير موجود: $ZipPath"
    }
}
else {
    $productionDir = Join-Path $ArtifactsRoot "production"
    if (-not (Test-Path -LiteralPath $productionDir)) {
        throw "مجلد artifacts\production غير موجود. شغّل build-production-package.ps1 أولاً."
    }

    $zips = @(Get-ChildItem -LiteralPath $productionDir -Filter "Uqeb-*.zip" -File |
        Sort-Object LastWriteTime -Descending)

    if ($zips.Count -eq 0) {
        throw "لا توجد حزمة Uqeb-*.zip في $productionDir. شغّل build-production-package.ps1 أولاً."
    }

    $ZipPath = $zips[0].FullName
    Write-DeployInfo ("الحزمة المختارة: " + $ZipPath)
}

$shaPath = Find-Sha256SidecarPath -ZipPath $ZipPath
if (-not (Test-Path -LiteralPath $shaPath)) {
    throw "ملف SHA256 غير موجود: $shaPath"
}

$expectedHash = Read-Sha256SidecarFile -Sha256FilePath $shaPath
$actualHash   = Get-FileSha256Hex -Path $ZipPath
if ($actualHash -ne $expectedHash) {
    throw "تجزئة SHA256 للحزمة غير مطابقة. الحزمة قد تكون تالفة."
}
Write-DeployInfo "SHA256 للحزمة مطابق."

# Read and validate manifest inside ZIP — all three fields are required
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipArchive  = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
$packageVersion    = ""
$packageCommit     = ""
$requiredMigration = ""
try {
    $manifestEntry = $zipArchive.Entries |
        Where-Object { $_.FullName -eq 'manifest.json' } |
        Select-Object -First 1
    if (-not $manifestEntry) {
        throw "manifest.json غير موجود في حزمة ZIP. الحزمة ربما تالفة أو بُنيت بإصدار قديم."
    }
    $stream = $manifestEntry.Open()
    $reader = New-Object System.IO.StreamReader $stream
    $manifestJson = $null
    try {
        $manifestJson = $reader.ReadToEnd() | ConvertFrom-Json
    }
    catch {
        throw "تعذر تفسير manifest.json كـ JSON: $($_.Exception.Message)"
    }
    finally { $reader.Dispose(); $stream.Dispose() }

    $packageVersion    = [string]$manifestJson.version
    $packageCommit     = [string]$manifestJson.commitSha
    $requiredMigration = [string]$manifestJson.minimumDatabaseMigration

    if ([string]::IsNullOrWhiteSpace($packageVersion)) {
        throw "حقل 'version' مفقود أو فارغ في manifest.json. الحزمة غير صالحة."
    }
    if ([string]::IsNullOrWhiteSpace($packageCommit)) {
        throw "حقل 'commitSha' مفقود أو فارغ في manifest.json. الحزمة غير صالحة."
    }
    if ([string]::IsNullOrWhiteSpace($requiredMigration)) {
        throw "حقل 'minimumDatabaseMigration' مفقود أو فارغ في manifest.json. الحزمة غير صالحة."
    }
}
finally {
    $zipArchive.Dispose()
}

Write-DeployInfo ("الإصدار: " + $packageVersion)
if ($packageCommit)     { Write-DeployInfo ("Commit: " + $packageCommit) }
if ($requiredMigration) { Write-DeployInfo ("آخر migration مطلوبة: " + $requiredMigration) }

Write-DeployStep "إنشاء مجلد النقل"

$transferName = "UqebDeploy-$packageVersion"
$transferDir  = Join-Path $TransferRoot $transferName

if (Test-Path -LiteralPath $transferDir) {
    Write-DeployInfo ("حذف مجلد النقل القديم: " + $transferDir)
    Remove-Item -LiteralPath $transferDir -Recurse -Force
}

$incomingDir   = Join-Path $transferDir "incoming"
$toolsDir      = Join-Path $transferDir "tools"
$deploymentDir = Join-Path $toolsDir "deployment"

Ensure-Directory $incomingDir
Ensure-Directory $toolsDir
Ensure-Directory $deploymentDir

Write-DeployStep "نسخ ملفات الحزمة"
Copy-Item -LiteralPath $ZipPath  -Destination (Join-Path $incomingDir ([System.IO.Path]::GetFileName($ZipPath))) -Force
Copy-Item -LiteralPath $shaPath  -Destination (Join-Path $incomingDir ([System.IO.Path]::GetFileName($shaPath))) -Force

Write-DeployStep "نسخ سكربتات النشر"
$scriptsToTools = @(
    "install-production-package.ps1",
    "install-scanner-bridge.ps1",
    "apply-migrations.ps1",
    "verify-deployment-health.ps1",
    "deploy-production-fast.ps1"
)
foreach ($script in $scriptsToTools) {
    $src = Join-Path $scriptDir $script
    if (-not (Test-Path -LiteralPath $src)) {
        throw "سكربت النشر الإلزامي غير موجود في repo: $script. تحقق من أن الفرع محدّث."
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $toolsDir $script) -Force
}

# verify-playwright-readiness.ps1 optional
$readinessScript = Join-Path $scriptDir "verify-playwright-readiness.ps1"
if (Test-Path -LiteralPath $readinessScript) {
    Copy-Item -LiteralPath $readinessScript -Destination (Join-Path $toolsDir "verify-playwright-readiness.ps1") -Force
}

Copy-Item -LiteralPath $commonPath -Destination (Join-Path $deploymentDir "Common.ps1") -Force

# Write a top-level launcher for convenience
$launcherPath = Join-Path $transferDir "deploy.ps1"
Set-Content -LiteralPath $launcherPath -Encoding UTF8 -Value @"
#Requires -Version 5.1
<#
  مشغّل سريع: ينسخ الأدوات إلى C:\UqebTools ثم يشغّل النشر السريع.
  شغّله من داخل مجلد النقل على جهاز الإنتاج كـ Administrator.
#>
`$ErrorActionPreference = 'Stop'
`$here = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$fast = Join-Path `$here 'tools\deploy-production-fast.ps1'
if (-not (Test-Path -LiteralPath `$fast)) {
    throw 'deploy-production-fast.ps1 غير موجود في مجلد tools.'
}
& `$fast -TransferDir `$here @args
"@

# Write README
$readmePath = Join-Path $transferDir "TRANSFER-README.txt"
$zipName = [System.IO.Path]::GetFileName($ZipPath)
Set-Content -LiteralPath $readmePath -Encoding UTF8 -Value @"
========================================
 Uqeb Production Transfer Package
 Version  : $packageVersion
 Commit   : $packageCommit
 Migration: $requiredMigration
========================================

على جهاز الإنتاج (Administrator PowerShell):

  1. انسخ مجلد '$transferName' إلى أي مكان مناسب.
  2. شغّل:

     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\deploy.ps1

  أو للتحكم الكامل:

     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\deploy-production-fast.ps1 -TransferDir .

  يتولى السكربت تلقائياً:
    - نقل الأدوات إلى C:\UqebTools
    - نقل الحزمة إلى C:\Uqeb\incoming
    - تطبيق migration إذا لزم (بعد backup تلقائي)
    - ترقية web + api بطريقة آمنة
    - فحص /health/ready و /health/live
    - طباعة تقرير GO/NO-GO

ملفات المجلد:
  incoming\$zipName         <- الحزمة
  incoming\$zipName.sha256.txt  <- SHA256 للتحقق
  tools\deploy-production-fast.ps1   <- سكربت النشر
  tools\install-production-package.ps1
  tools\install-scanner-bridge.ps1
  tools\apply-migrations.ps1
  tools\verify-deployment-health.ps1
  tools\deployment\Common.ps1
  deploy.ps1  <- مشغّل سريع

لتثبيت Scanner Bridge على جهاز Windows الذي يحتوي الماسح والمتصفح:

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\install-scanner-bridge.ps1 -PackagePath .\incoming\$zipName

ثم تحقق:

  Invoke-RestMethod http://127.0.0.1:5055/status
  Invoke-RestMethod http://127.0.0.1:5055/scanners
"@

Write-DeployStep "مجلد النقل جاهز"
Write-DeployInfo ("المسار: " + $transferDir)
Write-DeployInfo ""
Write-DeployInfo "انقل المجلد الآتي إلى جهاز الإنتاج ثم شغّل deploy.ps1:"
Write-DeployInfo ("  " + $transferDir)
Write-DeployInfo ""
Write-DeployInfo "أو اضغط نسخ ولصق:"
Write-DeployInfo ("  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\deploy.ps1")
