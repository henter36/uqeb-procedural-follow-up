#Requires -Version 5.1
<#
.SYNOPSIS
  نشر آمن لحزمة Uqeb جاهزة إلى جهاز إنتاج Windows.

.PARAMETER SourcePackagePath
  مسار جذر الحزمة التي تحتوي publish\api و publish\web

.PARAMETER InstallRoot
  مسار التثبيت على الإنتاج (افتراضي: C:\Uqeb)

.PARAMETER ScheduledTaskName
  اسم مهمة Windows المجدولة لتشغيل API (افتراضي: UqebApi). إن وُجدت تُوقف قبل النسخ وتُعاد بعده.

.EXAMPLE
  .\deploy-production.ps1 -SourcePackagePath "D:\Uqeb-release-20260617"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePackagePath,

    [string]$InstallRoot = "C:\Uqeb",

    [string]$ScheduledTaskName = "UqebApi"
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host ">> $Message" -ForegroundColor Cyan
}

function Write-Warn([string]$Message) {
    Write-Host "!! $Message" -ForegroundColor Yellow
}

function Get-SpaWebConfigContent {
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <defaultDocument enabled="true">
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>
    <httpErrors errorMode="Custom" existingResponse="Replace">
      <remove statusCode="404" />
      <error statusCode="404" path="/index.html" responseMode="ExecuteURL" />
    </httpErrors>
  </system.webServer>
  <location path="index.html">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="DisableCache" />
      </staticContent>
      <httpProtocol>
        <customHeaders>
          <add name="Cache-Control" value="no-cache, no-store, must-revalidate" />
          <add name="Pragma" value="no-cache" />
          <add name="Expires" value="0" />
        </customHeaders>
      </httpProtocol>
    </system.webServer>
  </location>
</configuration>
'@
}

function Stop-UqebApi {
    param([string]$TaskName)

    $hadTask = $false
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        $hadTask = $true
        Write-Step "إيقاف المهمة المجدولة: $TaskName"
        try {
            Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        } catch {
            Write-Warn "تعذر إيقاف المهمة المجدولة: $($_.Exception.Message)"
        }
    }

    Write-Step "التحقق من العمليات على المنفذ 5000"
    $connections = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue
    foreach ($conn in $connections) {
        $pid = $conn.OwningProcess
        if (-not $pid) { continue }
        try {
            $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Step "إيقاف العملية $($proc.ProcessName) (PID $pid) على المنفذ 5000"
                Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Warn "تعذر إيقاف PID $pid"
        }
    }

    Get-Process -Name "Uqeb.Api" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Step "إيقاف Uqeb.Api (PID $($_.Id))"
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
    return $hadTask
}

function Copy-TreeSafe {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExcludeFileNames = @()
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "المصدر غير موجود: $Source"
    }

    if (-not (Test-Path -LiteralPath $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    }

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($ExcludeFileNames -contains $_.Name) {
            Write-Step "تخطي (محفوظ محليًا): $($_.Name)"
            return
        }
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-TreeSafe -Source $_.FullName -Destination $target -ExcludeFileNames $ExcludeFileNames
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

# --- تحقق من المدخلات ---
$SourcePackagePath = (Resolve-Path -LiteralPath $SourcePackagePath).Path
$sourceApi = Join-Path $SourcePackagePath "publish\api"
$sourceWeb = Join-Path $SourcePackagePath "publish\web"

if (-not (Test-Path -LiteralPath $sourceApi)) {
    throw "لم يُعثر على publish\api داخل الحزمة: $sourceApi"
}
if (-not (Test-Path -LiteralPath $sourceWeb)) {
    throw "لم يُعثر على publish\web داخل الحزمة: $sourceWeb"
}

$destApi = Join-Path $InstallRoot "publish\api"
$destWeb = Join-Path $InstallRoot "publish\web"
$logsDir = Join-Path $InstallRoot "logs"

Write-Host ""
Write-Host "=== نشر Uqeb للإنتاج ===" -ForegroundColor Cyan
Write-Host "المصدر:  $SourcePackagePath"
Write-Host "الهدف:   $InstallRoot"
Write-Host ""

# --- إيقاف API ---
$restartTask = Stop-UqebApi -TaskName $ScheduledTaskName

# --- إنشاء المجلدات ---
foreach ($dir in @($InstallRoot, $destApi, $destWeb, $logsDir)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        Write-Step "إنشاء المجلد: $dir"
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# --- نسخ Backend (مع حماية appsettings.Production.json) ---
Write-Step "نسخ Backend إلى $destApi"
$excludeProdSettings = @()
$prodSettingsPath = Join-Path $destApi "appsettings.Production.json"
if (Test-Path -LiteralPath $prodSettingsPath) {
    $excludeProdSettings = @("appsettings.Production.json")
    Write-Step "appsettings.Production.json موجود — لن يُستبدل"
}
Copy-TreeSafe -Source $sourceApi -Destination $destApi -ExcludeFileNames $excludeProdSettings

# إذا لم يكن موجودًا في الإنتاج، انسخه من الحزمة إن وُجد
if (-not (Test-Path -LiteralPath $prodSettingsPath)) {
    $srcProd = Join-Path $sourceApi "appsettings.Production.json"
    if (Test-Path -LiteralPath $srcProd) {
        Write-Step "نسخ appsettings.Production.json لأول مرة"
        Copy-Item -LiteralPath $srcProd -Destination $prodSettingsPath -Force
    } else {
        Write-Warn "لا يوجد appsettings.Production.json — أنشئه يدويًا من appsettings.example.json"
    }
}

# --- نسخ Frontend ---
Write-Step "نسخ Frontend إلى $destWeb"
Copy-TreeSafe -Source $sourceWeb -Destination $destWeb

# --- web.config للـ SPA ---
$webConfigPath = Join-Path $destWeb "web.config"
Write-Step "كتابة web.config للواجهة"
Set-Content -LiteralPath $webConfigPath -Value (Get-SpaWebConfigContent) -Encoding UTF8

# --- التحقق من الملفات الأساسية ---
$requiredApi = @(
    (Join-Path $destApi "Uqeb.Api.dll"),
    (Join-Path $destApi "Uqeb.Api.runtimeconfig.json")
)
$requiredWeb = @(
    (Join-Path $destWeb "index.html"),
    (Join-Path $destWeb "assets")
)

$missing = @()
foreach ($f in ($requiredApi + $requiredWeb)) {
    if (-not (Test-Path -LiteralPath $f)) {
        $missing += $f
    }
}

if ($missing.Count -gt 0) {
    throw "ملفات مطلوبة ناقصة بعد النشر:`n - " + ($missing -join "`n - ")
}

Write-Step "التحقق: Uqeb.Api.dll, runtimeconfig, index.html, assets — OK"

# --- BUILD_INFO.txt ---
$buildInfoPath = Join-Path $InstallRoot "BUILD_INFO.txt"
$buildInfo = @"
Uqeb Production Deployment
==========================
DeployedAtUtc: $((Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")) UTC
DeployedAtLocal: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
SourcePackage: $SourcePackagePath
InstallRoot: $InstallRoot
ScheduledTask: $ScheduledTaskName
TargetFramework: net10.0
"@
Set-Content -LiteralPath $buildInfoPath -Value $buildInfo -Encoding UTF8
Write-Step "تم إنشاء BUILD_INFO.txt"

# --- إعادة تشغيل المهمة المجدولة ---
if ($restartTask) {
    Write-Step "إعادة تشغيل المهمة المجدولة: $ScheduledTaskName"
    try {
        Start-ScheduledTask -TaskName $ScheduledTaskName
        Start-Sleep -Seconds 3
        $info = Get-ScheduledTaskInfo -TaskName $ScheduledTaskName
        Write-Host "       آخر تشغيل: $($info.LastRunTime) — النتيجة: $($info.LastTaskResult)" -ForegroundColor DarkGray
    } catch {
        Write-Warn "تعذر إعادة تشغيل المهمة المجدولة: $($_.Exception.Message)"
        Write-Warn "شغّل API يدويًا أو راجع DEPLOYMENT_NOTES.md"
    }
} else {
    Write-Warn "لم تُعثر على مهمة مجدولة باسم '$ScheduledTaskName' — شغّل API يدويًا بعد النشر."
}

Write-Host ""
Write-Host "اكتمل النشر بنجاح." -ForegroundColor Green
Write-Host "الخطوات التالية:"
Write-Host "  1) تحقق من appsettings.Production.json و ConnectionStrings"
Write-Host "  2) شغّل: .\scripts\check-prerequisites.ps1"
Write-Host "  3) اختبر login: http://localhost:5000/api/auth/login"
Write-Host ""
