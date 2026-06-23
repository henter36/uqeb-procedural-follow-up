#Requires -Version 5.1
<#
.SYNOPSIS
  تثبيت حزمة إنتاج offline على جهاز Windows الإنتاج.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$ApiPath = "C:\Uqeb\publish\api",
    [string]$WebPath = "C:\Uqeb\publish\web",
    [string]$ConfigPath = "C:\Uqeb\config\appsettings.Production.json",
    [string]$TaskName = "UqebApi",
    [int]$ApiPort = 5000,
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$InstallRoot = "C:\Uqeb",
    [string]$ToolsRoot = "C:\UqebTools",
    [switch]$SkipFileBackup,
    [switch]$SkipDatabaseMigration
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    $commonPath = Join-Path $ToolsRoot "deployment\Common.ps1"
}
if (-not (Test-Path -LiteralPath $commonPath)) {
    throw "تعذر العثور على deployment\Common.ps1"
}
. $commonPath

$deploymentResult = "فشل"
$backupPath = ""
$databaseBackupPath = ""
$databaseBackupSizeBytes = 0
$databaseBackupCreatedAtUtc = ""
$databaseBackupSha256 = ""
$databaseBackupStatus = "لم يُنفَّذ"
$databaseRetentionDeleted = @()
$databaseStatus = "لم يُنفَّذ"
$apiHealth = "فشل"
$packageVersion = ""
$packageCommit = ""
$stagingPath = ""
$rollbackPerformed = $false
$configTarget = ""
$manualRestoreCommand = ""
$sqlInfo = $null

try {
    if (-not (Test-IsAdministrator)) {
        throw "يجب تشغيل السكربت كمسؤول (Administrator)."
    }

    Assert-DatabaseBackupNotBypassed

    if (-not (Test-Path -LiteralPath $PackagePath)) {
        throw "ملف الحزمة غير موجود: $PackagePath"
    }
    if ([System.IO.Path]::GetExtension($PackagePath) -ne ".zip") {
        throw "المسار المحدد ليس ملف ZIP. لا تمرّر مجلدًا إلى سكربت التثبيت."
    }

    $shaPath = Find-Sha256SidecarPath -ZipPath $PackagePath
    $expectedZipHash = Read-Sha256SidecarFile -Sha256FilePath $shaPath
    $actualZipHash = Get-FileSha256Hex -Path $PackagePath
    if ($actualZipHash -ne $expectedZipHash) {
        throw "تجزئة SHA256 للحزمة غير مطابقة. توقّف التثبيت."
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $stagingPath = Join-Path $InstallRoot ("staging\" + $stamp)
    Ensure-Directory $stagingPath

    Write-DeployStep "فك حزمة الإنتاج"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($PackagePath, $stagingPath)

    $requiredPaths = @(
        (Join-Path $stagingPath "manifest.json"),
        (Join-Path $stagingPath "api\Uqeb.Api.dll"),
        (Join-Path $stagingPath "web\index.html"),
        (Join-Path $stagingPath "database\migrations-idempotent.sql")
    )
    foreach ($required in $requiredPaths) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "حزمة ناقصة: $required"
        }
    }

    $manifest = Get-Content -LiteralPath (Join-Path $stagingPath "manifest.json") -Raw | ConvertFrom-Json
    Test-PackageManifestHashes -PackageRoot $stagingPath -Manifest $manifest
    $packageVersion = [string]$manifest.version
    $packageCommit = [string]$manifest.commitSha

    $sqlInfo = Get-SqlConnectionInfoFromSettings -SettingsPath $ConfigPath
    Write-DeployInfo ("خادم SQL: " + $sqlInfo.Server)
    Write-DeployInfo ("قاعدة البيانات: " + $sqlInfo.Database)

    if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        throw "مهمة الجدولة '$TaskName' غير موجودة."
    }

    $backupRoot = Join-Path $InstallRoot "backup"
    $databaseBackupDirectory = Join-Path $backupRoot "db"
    $releaseManifestPath = Join-Path (Split-Path $ApiPath -Parent) "release-manifest.json"
    $configTarget = Join-Path $ApiPath "appsettings.Production.json"
    $logsPath = Join-Path $InstallRoot "logs\api-runtime.log"
    $deployStartedAt = Get-Date

    Write-DeployStep "إنشاء نسخة احتياطية إلزامية لقاعدة البيانات"
    $databaseBackup = Invoke-ProductionDatabaseBackup `
        -Server $sqlInfo.Server `
        -Database $sqlInfo.Database `
        -BackupDirectory $databaseBackupDirectory `
        -Timestamp $stamp

    $databaseBackupPath = $databaseBackup.Path
    $databaseBackupSizeBytes = [long]$databaseBackup.SizeBytes
    $databaseBackupCreatedAtUtc = [string]$databaseBackup.CreatedAtUtc
    $databaseBackupSha256 = [string]$databaseBackup.Sha256
    $databaseBackupStatus = "نجح"
    $manualRestoreCommand = Get-ManualDatabaseRestoreCommand `
        -Server $sqlInfo.Server `
        -Database $sqlInfo.Database `
        -BackupPath $databaseBackupPath

    Write-DeployInfo ("مسار نسخة قاعدة البيانات: " + $databaseBackupPath)
    Write-DeployInfo ("حجم النسخة (بايت): " + $databaseBackupSizeBytes)
    Write-DeployInfo ("وقت إنشاء النسخة (UTC): " + $databaseBackupCreatedAtUtc)
    Write-DeployInfo ("تجزئة SHA256 للنسخة: " + $databaseBackupSha256)

    Write-DeployStep "إيقاف API"
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Stop-ApiListenersOnPort -Port $ApiPort
    if (-not (Wait-PortReleased -Port $ApiPort -TimeoutSec 30)) {
        throw "المنفذ $ApiPort ما زال مستخدماً بعد إيقاف API."
    }

    $backupPath = Join-Path $backupRoot ("before-" + $stamp)
    $backupApi = Join-Path $backupPath "api"
    $backupWeb = Join-Path $backupPath "web"
    $backupManifest = Join-Path $backupPath "release-manifest.json"

    if (-not $SkipFileBackup) {
        Write-DeployStep "إنشاء نسخة احتياطية للملفات"
        Ensure-Directory $backupApi
        Ensure-Directory $backupWeb
        if (Test-DirectoryHasContent $ApiPath) {
            Invoke-RobocopyWithoutMirror -Source $ApiPath -Destination $backupApi
        }
        if (Test-DirectoryHasContent $WebPath) {
            Invoke-RobocopyWithoutMirror -Source $WebPath -Destination $backupWeb
        }
        if (Test-Path -LiteralPath $releaseManifestPath) {
            Copy-Item -LiteralPath $releaseManifestPath -Destination $backupManifest -Force
        }
        if (Test-Path -LiteralPath $ConfigPath) {
            Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $backupPath "appsettings.Production.json") -Force
        }
    }
    else {
        Write-DeployInfo "تم تخطي النسخة الاحتياطية للملفات (-SkipFileBackup). نسخة قاعدة البيانات ما زالت إلزامية."
        $backupPath = "(متخطى)"
    }

    if (-not $SkipDatabaseMigration) {
        Write-DeployStep "تطبيق migrations"
        $migrationScript = Join-Path $stagingPath "scripts\apply-migrations.ps1"
        if (-not (Test-Path -LiteralPath $migrationScript)) {
            $migrationScript = Join-Path $ToolsRoot "apply-migrations.ps1"
        }
        if (-not (Test-Path -LiteralPath $migrationScript)) {
            throw "سكربت apply-migrations.ps1 غير موجود في الحزمة أو في C:\UqebTools."
        }

        & $migrationScript `
            -Server $sqlInfo.Server `
            -Database $sqlInfo.Database `
            -MigrationFile (Join-Path $stagingPath "database\migrations-idempotent.sql") `
            -ExpectedLatestMigration $manifest.minimumDatabaseMigration

        if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
            throw "فشل تطبيق migrations برمز $LASTEXITCODE."
        }
        $databaseStatus = "نجح"
    }
    else {
        Write-DeployInfo "تم تخطي migrations (-SkipDatabaseMigration). نسخة قاعدة البيانات نُفِّذت قبل ذلك."
        $databaseStatus = "متخطى"
    }

    Write-DeployStep "نسخ ملفات API و Web"
    Ensure-Directory $ApiPath
    Ensure-Directory $WebPath
    Copy-ApplicationPayload `
        -ApiSource (Join-Path $stagingPath "api") `
        -WebSource (Join-Path $stagingPath "web") `
        -ApiTarget $ApiPath `
        -WebTarget $WebPath

    if (Test-Path -LiteralPath $ConfigPath) {
        Copy-Item -LiteralPath $ConfigPath -Destination $configTarget -Force
    }
    else {
        throw "إعداد الإنتاج المعتمد غير موجود: $ConfigPath"
    }

    $releaseInfo = [ordered]@{
        deployedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        package = [System.IO.Path]::GetFileName($PackagePath)
        version = $packageVersion
        commitSha = $packageCommit
        minimumDatabaseMigration = [string]$manifest.minimumDatabaseMigration
        apiPath = $ApiPath
        webPath = $WebPath
        databaseBackup = [ordered]@{
            path = $databaseBackupPath
            sizeBytes = $databaseBackupSizeBytes
            createdAtUtc = $databaseBackupCreatedAtUtc
            sha256 = $databaseBackupSha256
        }
    }
    $releaseInfo | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $releaseManifestPath -Encoding UTF8

    Write-DeployStep "تشغيل API"
    Start-ScheduledTask -TaskName $TaskName
    if (-not (Test-PortListener -Port $ApiPort -TimeoutSec 45)) {
        throw "لم يبدأ API على المنفذ $ApiPort."
    }

    $healthScript = Join-Path $ToolsRoot "verify-deployment-health.ps1"
    if (-not (Test-Path -LiteralPath $healthScript)) {
        $healthScript = Join-Path $stagingPath "scripts\verify-deployment-health.ps1"
    }
    if (-not (Test-Path -LiteralPath $healthScript)) {
        throw "سكربت verify-deployment-health.ps1 غير موجود."
    }

    Write-DeployStep "فحص صحة API"
    & $healthScript -ApiBaseUrl $ApiBaseUrl -RetryCount 5 -RetryDelaySec 2
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "فشل فحص الصحة برمز $LASTEXITCODE."
    }
    $apiHealth = "نجح"

    $logErrors = Test-RecentLogErrors -LogPath $logsPath -Since $deployStartedAt
    if (@($logErrors).Count -gt 0) {
        throw ("تم العثور على أخطاء جديدة في السجل: " + ($logErrors -join " | "))
    }

    $databaseRetentionDeleted = Invoke-DatabaseBackupRetentionPolicy `
        -BackupDirectory $databaseBackupDirectory `
        -InstallRoot $InstallRoot `
        -MinimumKeepCount 10 `
        -LatestSuccessfulBackupPath $databaseBackupPath

    $deployedDir = Join-Path $InstallRoot "incoming\deployed"
    Ensure-Directory $deployedDir
    Move-Item -LiteralPath $PackagePath -Destination (Join-Path $deployedDir (Split-Path -Leaf $PackagePath)) -Force
    if (Test-Path -LiteralPath $shaPath) {
        Move-Item -LiteralPath $shaPath -Destination (Join-Path $deployedDir (Split-Path -Leaf $shaPath)) -Force
    }

    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    $deploymentResult = "نجح"
}
catch {
    Write-DeployError ("فشل النشر: " + $_.Exception.Message)

    if ($databaseBackupPath -and $sqlInfo) {
        if (-not $manualRestoreCommand) {
            $manualRestoreCommand = Get-ManualDatabaseRestoreCommand `
                -Server $sqlInfo.Server `
                -Database $sqlInfo.Database `
                -BackupPath $databaseBackupPath
        }
        Write-DeployInfo "أمر الاستعادة اليدوي لقاعدة البيانات:"
        Write-DeployInfo $manualRestoreCommand
        Write-DeployInfo "لا يوجد استعادة تلقائية لقاعدة البيانات."
    }

    if (-not $SkipFileBackup -and $backupPath -and $backupPath -ne "(متخطى)") {
        $rollbackPerformed = Invoke-DeploymentFileRollback `
            -TaskName $TaskName `
            -ApiPort $ApiPort `
            -BackupApi (Join-Path $backupPath "api") `
            -BackupWeb (Join-Path $backupPath "web") `
            -ApiTarget $ApiPath `
            -WebTarget $WebPath `
            -ConfigTarget $configTarget `
            -ConfigSource (Join-Path $backupPath "appsettings.Production.json")

        if ($rollbackPerformed) {
            Write-DeployInfo "تم استرجاع ملفات API/Web من النسخة الاحتياطية. لا يوجد rollback تلقائي لقاعدة البيانات."
        }
    }

    exit 1
}
finally {
    Write-DeployStep "تقرير النشر النهائي"
    Write-DeployInfo ("الحزمة: " + $PackagePath)
    Write-DeployInfo ("الإصدار: " + $(if ($packageVersion) { $packageVersion } else { "غير معروف" }))
    Write-DeployInfo ("Commit SHA: " + $(if ($packageCommit) { $packageCommit } else { "غير متوفر" }))
    Write-DeployInfo ("حالة نسخة قاعدة البيانات: " + $databaseBackupStatus)
    Write-DeployInfo ("مسار نسخة قاعدة البيانات: " + $(if ($databaseBackupPath) { $databaseBackupPath } else { "غير متوفر" }))
    if ($databaseBackupPath) {
        Write-DeployInfo ("حجم نسخة قاعدة البيانات (بايت): " + $databaseBackupSizeBytes)
        Write-DeployInfo ("وقت نسخة قاعدة البيانات (UTC): " + $databaseBackupCreatedAtUtc)
        Write-DeployInfo ("SHA256 لنسخة قاعدة البيانات: " + $databaseBackupSha256)
    }
    Write-DeployInfo ("حالة migrations: " + $databaseStatus)
    Write-DeployInfo ("صحة API: " + $apiHealth)
    Write-DeployInfo ("مسار الواجهة: " + $WebPath)
    Write-DeployInfo ("مسار النسخة الاحتياطية للملفات: " + $backupPath)
    if (@($databaseRetentionDeleted).Count -gt 0) {
        Write-DeployInfo ("نسخ قاعدة بيانات محذوفة بسياسة الاحتفاظ: " + ($databaseRetentionDeleted -join " | "))
    }
    Write-DeployInfo ("نتيجة النشر: " + $deploymentResult)
    if ($rollbackPerformed) {
        Write-DeployInfo "تم تنفيذ rollback للملفات فقط."
    }
    if ($manualRestoreCommand -and $deploymentResult -ne "نجح") {
        Write-DeployInfo "أمر الاستعادة اليدوي لقاعدة البيانات:"
        Write-DeployInfo $manualRestoreCommand
    }
}

if ($deploymentResult -ne "نجح") {
    exit 1
}
