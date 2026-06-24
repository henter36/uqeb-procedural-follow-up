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
    [string]$ApiBaseUrl = "",
    [string]$InstallRoot = "C:\Uqeb",
    [string]$ToolsRoot = "C:\UqebTools",
    [string]$PlaywrightBrowsersPath = "C:\Uqeb\tools\ms-playwright",
    [switch]$SkipFileBackup,
    [switch]$ApplyDatabaseMigration
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

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = "http://localhost:$ApiPort"
}

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
$playwrightPreflight = $null
$browserPreviousPath = ""
$runApiPath = Join-Path $InstallRoot "run-api.cmd"
$logsPath = Join-Path $InstallRoot "logs\api-runtime.log"
$releaseManifestPath = Join-Path (Split-Path $ApiPath -Parent) "release-manifest.json"
$rollbackStatePath = Get-RollbackStatePath -InstallRoot $InstallRoot
$currentApiPath = Join-Path $InstallRoot "current\api"
$deployStartedAt = [DateTime]::UtcNow

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
    Expand-ArchiveSafely -ArchivePath $PackagePath -DestinationPath $stagingPath

    $requiredPaths = @(
        (Join-Path $stagingPath "manifest.json"),
        (Join-Path $stagingPath "api\Uqeb.Api.dll"),
        (Join-Path $stagingPath "api\playwright.ps1"),
        (Join-Path $stagingPath "web\index.html"),
        (Join-Path $stagingPath "database\migrations-idempotent.sql"),
        (Join-Path $stagingPath "browsers\playwright-browser-manifest.json")
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

    Write-DeployStep "التحقق من حمولة Chromium قبل إيقاف الخدمة"
    $playwrightPreflight = Test-PlaywrightPackagePreflight -PackageRoot $stagingPath -Manifest $manifest

    $sqlInfo = Get-SqlConnectionInfoFromSettings -SettingsPath $ConfigPath
    Write-DeployInfo ("خادم SQL: " + $sqlInfo.Server)
    Write-DeployInfo ("قاعدة البيانات: " + $sqlInfo.Database)

    Write-DeployStep "التحقق من migration المطلوبة قبل الترقية"
    Test-RequiredMigrationApplied `
        -ConnectionString $sqlInfo.ConnectionString `
        -RequiredMigrationId ([string]$manifest.minimumDatabaseMigration)
    Write-DeployInfo ("migration المطلوبة مطبقة: " + $manifest.minimumDatabaseMigration)

    if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        throw "مهمة الجدولة '$TaskName' غير موجودة."
    }

    $backupRoot = Join-Path $InstallRoot "backup"
    $databaseBackupDirectory = Join-Path $backupRoot "db"
    $configTarget = Join-Path $currentApiPath "appsettings.Production.json"

    if ($ApplyDatabaseMigration) {
        Write-DeployStep "إنشاء نسخة احتياطية إلزامية لقاعدة البيانات"
        $databaseBackup = Invoke-ProductionDatabaseBackup `
            -Server $sqlInfo.Server `
            -Database $sqlInfo.Database `
            -ConnectionString $sqlInfo.ConnectionString `
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
    }
    else {
        Write-DeployInfo "لم يُطلب ApplyDatabaseMigration؛ لن يُنشئ المثبت نسخة قاعدة بيانات ولا يطبّق migrations."
        $databaseBackupStatus = "متخطى"
        $databaseStatus = "متخطى"
    }

    Write-DeployStep "إيقاف API"
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Stop-ApiListenersOnPort -Port $ApiPort
    if (-not (Wait-PortReleased -Port $ApiPort -TimeoutSec 30)) {
        throw "المنفذ $ApiPort ما زال مستخدماً بعد إيقاف API."
    }

    $backupPath = Join-Path $backupRoot ("before-" + $stamp)
    $backupApi = Join-Path $backupPath "api"
    $backupWeb = Join-Path $backupPath "web"
    $backupBrowsers = Join-Path $backupPath "browsers"
    $backupManifest = Join-Path $backupPath "release-manifest.json"

    if (-not $SkipFileBackup) {
        Write-DeployStep "إنشاء نسخة احتياطية للملفات (legacy before-*)"
        Ensure-Directory $backupApi
        Ensure-Directory $backupWeb
        Ensure-Directory $backupBrowsers
        if (Test-DirectoryHasContent $currentApiPath) {
            Invoke-RobocopySafe -Source $currentApiPath -Destination $backupApi -TargetType Api
        }
        elseif (Test-DirectoryHasContent $ApiPath) {
            Invoke-RobocopySafe -Source $ApiPath -Destination $backupApi -TargetType Api
        }
        if (Test-DirectoryHasContent $WebPath) {
            Invoke-RobocopySafe -Source $WebPath -Destination $backupWeb -TargetType Web
        }
        if (Test-DirectoryHasContent $PlaywrightBrowsersPath) {
            Invoke-RobocopySafe -Source $PlaywrightBrowsersPath -Destination $backupBrowsers -TargetType Web
        }
        if (Test-Path -LiteralPath $releaseManifestPath) {
            Copy-Item -LiteralPath $releaseManifestPath -Destination $backupManifest -Force
        }
        if (Test-Path -LiteralPath $rollbackStatePath) {
            Copy-Item -LiteralPath $rollbackStatePath -Destination (Join-Path $backupPath "rollback-state.json") -Force
        }
        if (Test-Path -LiteralPath $ConfigPath) {
            Copy-Item -LiteralPath $ConfigPath -Destination (Join-Path $backupPath "appsettings.Production.json") -Force
        }
    }
    else {
        Write-DeployInfo "تم تخطي النسخة الاحتياطية للملفات (-SkipFileBackup). نسخة قاعدة البيانات ما زالت إلزامية."
        $backupPath = "(متخطى)"
    }

    if ($ApplyDatabaseMigration) {
        Write-DeployStep "تطبيق migrations"
        $migrationScript = Join-Path $stagingPath "scripts\apply-migrations.ps1"
        if (-not (Test-Path -LiteralPath $migrationScript)) {
            $migrationScript = Join-Path $ToolsRoot "apply-migrations.ps1"
        }
        if (-not (Test-Path -LiteralPath $migrationScript)) {
            throw "سكربت apply-migrations.ps1 غير موجود في الحزمة أو في C:\UqebTools."
        }

        & $migrationScript `
            -SettingsPath $ConfigPath `
            -MigrationFile (Join-Path $stagingPath "database\migrations-idempotent.sql") `
            -ExpectedLatestMigration $manifest.minimumDatabaseMigration

        $databaseStatus = "نجح"
    }

    Write-DeployStep "ترقية الإصدار عبر releases/current"
    $promotion = Install-StagedReleaseToProduction `
        -StagingPath $stagingPath `
        -InstallRoot $InstallRoot `
        -Version $packageVersion `
        -ConfigPath $ConfigPath `
        -PackagePath $PackagePath `
        -PackageCommit $packageCommit

    $configTarget = $promotion.ConfigTarget
    $releasePaths = $promotion.Paths

    Write-DeployStep "تثبيت Chromium في مسار الإنتاج"
    $packageBrowsersSource = Join-Path $stagingPath "browsers"
    $browserPreviousPath = Install-PlaywrightBrowserToProduction `
        -PackageBrowsersSource $packageBrowsersSource `
        -PlaywrightBrowsersPath $PlaywrightBrowsersPath
    $installedExecutable = Test-PlaywrightBrowserPayload `
        -BrowsersRoot $PlaywrightBrowsersPath `
        -ExpectedExecutableRelativePath $playwrightPreflight.ExecutableRelativePath `
        -ExpectedExecutableSha256 ([string]$manifest.playwright.browserExecutableSha256)

    Write-DeployStep "تحديث ملف تشغيل API"
    Ensure-Directory (Split-Path -Parent $runApiPath)
    Ensure-Directory (Split-Path -Parent $logsPath)
    Write-ApiRunScript `
        -RunScriptPath $runApiPath `
        -ApiPath $currentApiPath `
        -ApiPort $ApiPort `
        -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
        -LogPath $logsPath

    $releaseInfo = [ordered]@{
        deployedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        package = [System.IO.Path]::GetFileName($PackagePath)
        version = $packageVersion
        commitSha = $packageCommit
        promotionModel = "releases-current-v1"
        minimumDatabaseMigration = [string]$manifest.minimumDatabaseMigration
        releaseRoot = $releasePaths.ReleaseRoot
        currentApiPath = $releasePaths.CurrentApi
        currentWebPath = $releasePaths.CurrentWeb
        apiPath = $ApiPath
        webPath = $WebPath
        runApiScript = $runApiPath
        rollbackState = $rollbackStatePath
        databaseBackup = [ordered]@{
            path = $databaseBackupPath
            sizeBytes = $databaseBackupSizeBytes
            createdAtUtc = $databaseBackupCreatedAtUtc
            sha256 = $databaseBackupSha256
        }
        playwright = [ordered]@{
            browser = "chromium"
            browserPath = $PlaywrightBrowsersPath
            executablePath = $installedExecutable.FullPath
            browserExecutableSha256 = (Get-FileSha256Hex -Path $installedExecutable.FullPath)
            verifiedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
    }
    Ensure-Directory (Split-Path -Parent $releaseManifestPath)
    $releaseInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $releaseManifestPath -Encoding UTF8
    Ensure-Directory $releasePaths.ReleaseRoot
    $releaseInfo | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $releasePaths.ReleaseRoot "release-manifest.json") -Encoding UTF8

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
    & $healthScript `
        -ApiBaseUrl $ApiBaseUrl `
        -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
        -ExpectedBrowserExecutableSha256 ([string]$manifest.playwright.browserExecutableSha256) `
        -RetryCount 5 `
        -RetryDelaySec 2
    $apiHealth = "نجح"

    $logErrors = Test-RecentLogErrors -LogPath $logsPath -Since $deployStartedAt
    if (@($logErrors).Count -gt 0) {
        throw ("تم العثور على أخطاء جديدة في السجل: " + ($logErrors -join " | "))
    }

    Remove-DirectoryIfExists -Path $browserPreviousPath

    if ($ApplyDatabaseMigration -and $databaseBackupPath) {
        $databaseRetentionDeleted = Invoke-DatabaseBackupRetentionPolicy `
            -BackupDirectory $databaseBackupDirectory `
            -InstallRoot $InstallRoot `
            -MinimumKeepCount 10 `
            -LatestSuccessfulBackupPath $databaseBackupPath
    }

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

    $releaseRollback = Invoke-ReleaseRollbackFromState `
        -InstallRoot $InstallRoot `
        -TaskName $TaskName `
        -ApiPort $ApiPort `
        -ConfigPath (Join-Path $backupPath "appsettings.Production.json") `
        -ReleaseManifestPath $releaseManifestPath
    if ($releaseRollback) {
        $rollbackPerformed = $true
        Write-DeployInfo "تم استرجاع الإصدار السابق من rollback-state.json."
    }

    if (-not $rollbackPerformed -and -not $SkipFileBackup -and $backupPath -and $backupPath -ne "(متخطى)") {
        $rollbackPerformed = Invoke-DeploymentFileRollback `
            -TaskName $TaskName `
            -ApiPort $ApiPort `
            -BackupApi (Join-Path $backupPath "api") `
            -BackupWeb (Join-Path $backupPath "web") `
            -ApiTarget $currentApiPath `
            -WebTarget (Join-Path $InstallRoot "current\web") `
            -ConfigTarget $configTarget `
            -ConfigSource (Join-Path $backupPath "appsettings.Production.json") `
            -BackupBrowsers (Join-Path $backupPath "browsers") `
            -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
            -ReleaseManifestPath $releaseManifestPath `
            -BackupReleaseManifest (Join-Path $backupPath "release-manifest.json")

        if ($rollbackPerformed) {
            try {
                Sync-PublishCompatibilityLinks -InstallRoot $InstallRoot
            }
            catch {
                Write-DeployInfo ("تعذر تحديث روابط publish بعد rollback: " + $_.Exception.Message)
            }
            Write-DeployInfo "تم استرجاع ملفات API/Web/Chromium من النسخة الاحتياطية. لا يوجد rollback تلقائي لقاعدة البيانات."
        }
    }

    exit 1
}
finally {
    Write-DeployStep "تقرير النشر النهائي"
    Write-DeployInfo ("الحزمة: " + $PackagePath)
    Write-DeployInfo ("الإصدار: " + $(if ($packageVersion) { $packageVersion } else { "غير معروف" }))
    Write-DeployInfo ("Commit SHA: " + $(if ($packageCommit) { $packageCommit } else { "غير متوفر" }))
    Write-DeployInfo ("PLAYWRIGHT_BROWSERS_PATH: " + $PlaywrightBrowsersPath)
    Write-DeployInfo ("حالة نسخة قاعدة البيانات: " + $databaseBackupStatus)
    Write-DeployInfo ("مسار نسخة قاعدة البيانات: " + $(if ($databaseBackupPath) { $databaseBackupPath } else { "غير متوفر" }))
    if ($databaseBackupPath) {
        Write-DeployInfo ("حجم نسخة قاعدة البيانات (بايت): " + $databaseBackupSizeBytes)
        Write-DeployInfo ("وقت نسخة قاعدة البيانات (UTC): " + $databaseBackupCreatedAtUtc)
        Write-DeployInfo ("SHA256 لنسخة قاعدة البيانات: " + $databaseBackupSha256)
    }
    Write-DeployInfo ("حالة migrations: " + $databaseStatus)
    Write-DeployInfo ("صحة API: " + $apiHealth)
    Write-DeployInfo ("مسار rollback-state: " + $rollbackStatePath)
    Write-DeployInfo ("مسار current API: " + $currentApiPath)
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
