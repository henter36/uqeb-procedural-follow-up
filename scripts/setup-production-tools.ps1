#Requires -Version 5.1
<#
.SYNOPSIS
  إعداد أدوات النشر لأول مرة على جهاز الإنتاج.
#>

[CmdletBinding()]
param(
    [string]$ToolsRoot = "C:\UqebTools",
    [string]$InstallRoot = "C:\Uqeb",
    [string]$TaskName = "UqebApi"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
. $commonPath

if (-not (Test-IsAdministrator)) {
    throw "يجب تشغيل السكربت كمسؤول (Administrator)."
}

$directories = @(
    $ToolsRoot,
    (Join-Path $InstallRoot "incoming"),
    (Join-Path $InstallRoot "incoming\deployed"),
    (Join-Path $InstallRoot "staging"),
    (Join-Path $InstallRoot "releases"),
    (Join-Path $InstallRoot "current"),
    (Join-Path $InstallRoot "backup"),
    (Join-Path $InstallRoot "backup\db"),
    (Join-Path $InstallRoot "logs"),
    (Join-Path $InstallRoot "publish\api"),
    (Join-Path $InstallRoot "publish\web"),
    (Join-Path $InstallRoot "config"),
    (Join-Path $InstallRoot "tools\ms-playwright")
)

Write-DeployStep "إنشاء مجلدات الإنتاج"
foreach ($dir in $directories) {
    Ensure-Directory $dir
    Write-DeployInfo ("تم التأكد من: " + $dir)
}

Ensure-Directory (Join-Path $ToolsRoot "deployment")
$filesToCopy = @(
    @{ Source = "install-production-package.ps1"; Target = "install-production-package.ps1" },
    @{ Source = "apply-migrations.ps1"; Target = "apply-migrations.ps1" },
    @{ Source = "verify-deployment-health.ps1"; Target = "verify-deployment-health.ps1" },
    @{ Source = "verify-playwright-readiness.ps1"; Target = "verify-playwright-readiness.ps1" },
    @{ Source = "deployment\Common.ps1"; Target = "deployment\Common.ps1" }
)

Write-DeployStep "نسخ أدوات النشر إلى C:\UqebTools"
foreach ($item in $filesToCopy) {
    $source = Join-Path $PSScriptRoot $item.Source
    $target = Join-Path $ToolsRoot $item.Target
    if (-not (Test-Path -LiteralPath $source)) {
        throw "ملف الأداة غير موجود في المستودع: $($item.Source)"
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    Write-DeployInfo ("تم النسخ: " + $target)
}

$taskExists = $null -ne (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)
$sqlService = Get-Service -Name "MSSQL`$SQLEXPRESS" -ErrorAction SilentlyContinue
if (-not $sqlService) {
    $sqlService = Get-Service -Name "MSSQLSERVER" -ErrorAction SilentlyContinue
}

Write-DeployStep "تقرير جاهزية الإنتاج"
Write-DeployInfo ("مهمة الجدولة '$TaskName': " + ($(if ($taskExists) { "موجودة" } else { "غير موجودة" })))
Write-DeployInfo ("خدمة SQL Server: " + ($(if ($sqlService) { $sqlService.Status.ToString() + " (" + $sqlService.Name + ")" } else { "غير مكتشفة" })))
Write-DeployInfo "لم يتم تعديل أي أسرار إنتاج."
Write-DeployInfo "انسخ حزم ZIP وملفات SHA256 إلى C:\Uqeb\incoming ثم شغّل install-production-package.ps1 (المسار المعتمد)."
Write-DeployInfo "deploy-production-v2.ps1 legacy: استخدم install-production-package.ps1 للحزم ZIP."

if (-not $taskExists) {
    Write-DeployInfo "تحذير: يجب إعداد مهمة UqebApi قبل أول نشر."
    exit 2
}

if (-not $sqlService) {
    Write-DeployInfo "تحذير: لم يتم العثور على SQL Server Express أو SQL Server."
    exit 2
}

Write-DeployInfo "الجهاز جاهز لاستقبال حزم النشر offline."
