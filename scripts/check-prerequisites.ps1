#Requires -Version 5.1
<#
.SYNOPSIS
  فحص متطلبات تشغيل Uqeb في الإنتاج (قراءة فقط — لا يغيّر النظام).

.DESCRIPTION
  يتحقق من IIS وSQL Server وASP.NET Core Runtime والمجلدات المتوقعة.
  شغّل كمسؤول للحصول على نتائج أدق لميزات IIS.
#>

param(
    [string]$InstallRoot = "C:\Uqeb"
)

$ErrorActionPreference = "Continue"
$allOk = $true

function Write-Check {
    param(
        [string]$Label,
        [bool]$Passed,
        [string]$Detail = ""
    )
    if ($Passed) {
        Write-Host "[ OK ] $Label" -ForegroundColor Green
        if ($Detail) { Write-Host "       $Detail" -ForegroundColor DarkGray }
    } else {
        Write-Host "[ !! ] $Label" -ForegroundColor Red
        if ($Detail) { Write-Host "       $Detail" -ForegroundColor Yellow }
        $script:allOk = $false
    }
}

Write-Host ""
Write-Host "=== فحص متطلبات Uqeb ===" -ForegroundColor Cyan
Write-Host "مسار التثبيت المتوقع: $InstallRoot"
Write-Host ""

# --- IIS ---
$iisOk = $false
$iisDetail = ""

try {
    $w3svc = Get-Service -Name "W3SVC" -ErrorAction SilentlyContinue
    if ($w3svc -and $w3svc.Status -eq "Running") {
        $iisOk = $true
        $iisDetail = "خدمة W3SVC تعمل."
    } elseif ($w3svc) {
        $iisDetail = "خدمة W3SVC موجودة لكنها متوقفة: $($w3svc.Status)"
    }
} catch {
    $iisDetail = "تعذر فحص خدمة W3SVC."
}

if (-not $iisOk) {
  try {
    $optional = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -ErrorAction SilentlyContinue
    if ($optional -and $optional.State -eq "Enabled") {
      $iisOk = $true
      $iisDetail = "دور IIS-WebServerRole مفعّل (قد تحتاج تشغيل W3SVC)."
    }
  } catch { }

  try {
    $feature = Get-WindowsFeature -Name Web-Server -ErrorAction SilentlyContinue
    if ($feature -and $feature.Installed) {
      $iisOk = $true
      $iisDetail = "ميزة Web-Server مثبتة على Windows Server."
    }
  } catch { }
}

Write-Check -Label "IIS (واجهة الويب)" -Passed $iisOk -Detail $iisDetail

# ميزات IIS الموصى بها
$recommendedFeatures = @(
    @{ Client = "IIS-StaticContent"; Server = "Web-Static-Content"; Label = "Static Content" },
    @{ Client = "IIS-DefaultDocument"; Server = "Web-Default-Document"; Label = "Default Document" },
    @{ Client = "IIS-HttpErrors"; Server = "Web-Http-Errors"; Label = "HttpErrors" }
)

foreach ($feat in $recommendedFeatures) {
    $featOk = $false
    try {
        $opt = Get-WindowsOptionalFeature -Online -FeatureName $feat.Client -ErrorAction SilentlyContinue
        if ($opt -and $opt.State -eq "Enabled") { $featOk = $true }
    } catch { }
    if (-not $featOk) {
        try {
            $winFeat = Get-WindowsFeature -Name $feat.Server -ErrorAction SilentlyContinue
            if ($winFeat -and $winFeat.Installed) { $featOk = $true }
        } catch { }
    }
    Write-Check -Label "IIS: $($feat.Label)" -Passed $featOk -Detail $(if (-not $featOk) { "يُنصح بتفعيلها لـ SPA." } else { "" })
}

# --- SQL Server ---
$sqlServices = @("MSSQLSERVER", "MSSQL`$SQLEXPRESS")
$foundSql = @()
foreach ($name in $sqlServices) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc) {
        $foundSql += "$name ($($svc.Status))"
    }
}

$sqlOk = $foundSql.Count -gt 0
$sqlDetail = if ($sqlOk) {
    "خدمات SQL: " + ($foundSql -join ", ")
} else {
    "لم تُعثر على MSSQLSERVER ولا MSSQL`$SQLEXPRESS. راجع PREREQUISITES.md لفرق localhost و localhost\SQLEXPRESS."
}
Write-Check -Label "SQL Server" -Passed $sqlOk -Detail $sqlDetail

# --- dotnet runtime ---
$dotnetOk = $false
$dotnetDetail = ""
try {
    $runtimes = & dotnet --list-runtimes 2>&1
    if ($LASTEXITCODE -eq 0) {
        $aspNet10 = $runtimes | Where-Object { $_ -match "Microsoft\.AspNetCore\.App 10\." }
        if ($aspNet10) {
            $dotnetOk = $true
            $dotnetDetail = ($aspNet10 | Select-Object -First 1).Trim()
        } else {
            $dotnetDetail = "dotnet موجود لكن Microsoft.AspNetCore.App 10.x غير مثبت. ثبّت ASP.NET Core Runtime 10."
        }
    } else {
        $dotnetDetail = "أمر dotnet غير متاح في PATH."
    }
} catch {
    $dotnetDetail = "تعذر تشغيل dotnet: $($_.Exception.Message)"
}
Write-Check -Label "ASP.NET Core Runtime 10.x" -Passed $dotnetOk -Detail $dotnetDetail

# --- المجلدات ---
$paths = @(
    $InstallRoot,
    (Join-Path $InstallRoot "publish\api"),
    (Join-Path $InstallRoot "publish\web")
)

foreach ($p in $paths) {
    $exists = Test-Path -LiteralPath $p
    Write-Check -Label "المجلد: $p" -Passed $exists -Detail $(if (-not $exists) { "سيُنشأ عند أول نشر." } else { "" })
}

# ملفات رئيسية إن وُجدت
$apiDll = Join-Path $InstallRoot "publish\api\Uqeb.Api.dll"
$indexHtml = Join-Path $InstallRoot "publish\web\index.html"
if (Test-Path $apiDll) {
    Write-Check -Label "Uqeb.Api.dll" -Passed $true -Detail $apiDll
} elseif (Test-Path (Join-Path $InstallRoot "publish\api")) {
    Write-Check -Label "Uqeb.Api.dll" -Passed $false -Detail "المجلد موجود لكن DLL غير منشور بعد."
}

if (Test-Path $indexHtml) {
    Write-Check -Label "index.html (الواجهة)" -Passed $true -Detail $indexHtml
} elseif (Test-Path (Join-Path $InstallRoot "publish\web")) {
    Write-Check -Label "index.html (الواجهة)" -Passed $false -Detail "المجلد موجود لكن الواجهة غير منشورة بعد."
}

# --- المنفذ 5000 ---
$port5000 = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
if ($port5000) {
    $pids = ($port5000 | Select-Object -ExpandProperty OwningProcess -Unique) -join ", "
    Write-Host "[ .. ] المنفذ 5000 مستخدم حاليًا (PID: $pids)" -ForegroundColor Yellow
    Write-Host "       تأكد من عدم وجود نسخة API قديمة قبل النشر." -ForegroundColor DarkGray
} else {
    Write-Check -Label "المنفذ 5000" -Passed $true -Detail "غير مستخدم — جاهز لتشغيل API."
}

Write-Host ""
if ($allOk) {
    Write-Host "النتيجة: المتطلبات الأساسية متوفرة أو قريبة من الجاهزية." -ForegroundColor Green
} else {
    Write-Host "النتيجة: هناك عناصر ناقصة — راجع PREREQUISITES.md و DEPLOYMENT_NOTES.md." -ForegroundColor Yellow
}
Write-Host ""
exit $(if ($allOk) { 0 } else { 1 })
