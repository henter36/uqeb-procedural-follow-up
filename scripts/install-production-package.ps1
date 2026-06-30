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
    [string]$ApiBindAddress = "10.0.177.17",
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

Assert-ValidApiBindAddress -ApiBindAddress $ApiBindAddress

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = "http://${ApiBindAddress}:$ApiPort"
}
if ($ApplyDatabaseMigration) {
    Write-Warning '-ApplyDatabaseMigration is deprecated and no longer required. Migration execution is determined automatically from manifest.minimumDatabaseMigration.'
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
$packageValidated = $false
$backupVerified = $false
$migrationsApplied = $false
$scheduledTaskStopped = $false
$listenersStopped = $false
$portReleased = $false
$promotionStarted = $false
$promotionCompleted = $false
$requiredMigrationMissing = $false
$packageArchiveStatus = "لم تُنفَّذ"
$scheduledTaskBefore = $null
$scheduledTaskAfter = $null
$scheduledTaskActionValid = $false
$scheduledTaskReconcileStatus = "لم يُنفَّذ"

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
    $requiredMigration = [string]$manifest.minimumDatabaseMigration
    if ([string]::IsNullOrWhiteSpace($requiredMigration)) {
        throw "manifest.minimumDatabaseMigration غير موجود أو فارغ."
    }

    Write-DeployStep "التحقق من حمولة Chromium قبل إيقاف الخدمة"
    $playwrightPreflight = Test-PlaywrightPackagePreflight -PackageRoot $stagingPath -Manifest $manifest

    $sqlInfo = Get-SqlConnectionInfoFromSettings -SettingsPath $ConfigPath
    Write-DeployInfo ("خادم SQL: " + $sqlInfo.Server)
    Write-DeployInfo ("قاعدة البيانات: " + $sqlInfo.Database)
    $packageValidated = $true

    if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
        Write-DeployInfo "مهمة الجدولة '$TaskName' غير موجودة وسيتم إنشاؤها بعد تحديث run-api.cmd."
    }

    $backupRoot = Join-Path $InstallRoot "backup"
    $databaseBackupDirectory = Join-Path $backupRoot "db"
    $configTarget = Join-Path $currentApiPath "appsettings.Production.json"

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
    $backupVerified = $true
    $manualRestoreCommand = Get-ManualDatabaseRestoreCommand `
        -Server $sqlInfo.Server `
        -Database $sqlInfo.Database `
        -BackupPath $databaseBackupPath

    Write-DeployInfo ("مسار نسخة قاعدة البيانات: " + $databaseBackupPath)
    Write-DeployInfo ("حجم النسخة (بايت): " + $databaseBackupSizeBytes)
    Write-DeployInfo ("وقت إنشاء النسخة (UTC): " + $databaseBackupCreatedAtUtc)
    Write-DeployInfo ("تجزئة SHA256 للنسخة: " + $databaseBackupSha256)

    Write-DeployStep "فحص migration المطلوبة"
    $requiredMigrationApplied = Test-RequiredMigrationPresent `
        -ConnectionString $sqlInfo.ConnectionString `
        -RequiredMigrationId $requiredMigration
    if ($requiredMigrationApplied) {
        Write-DeployInfo 'Required migration already applied; migration execution skipped.'
        $databaseStatus = "مطبقة مسبقًا"
    }
    else {
        $requiredMigrationMissing = $true
        $databaseStatus = "مطلوبة"
        Write-DeployInfo ("Required migration is missing and will be applied automatically: " + $requiredMigration)
    }

    $healthScript = Join-Path $ToolsRoot "verify-deployment-health.ps1"
    if (-not (Test-Path -LiteralPath $healthScript)) {
        $healthScript = Join-Path $stagingPath "scripts\verify-deployment-health.ps1"
    }
    if (-not (Test-Path -LiteralPath $healthScript)) {
        throw "سكربت verify-deployment-health.ps1 غير موجود."
    }

    Write-DeployStep "إيقاف API"
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $scheduledTaskStopped = $true
    Stop-ApiListenersOnPort -Port $ApiPort
    $listenersStopped = $true
    if (-not (Wait-PortReleased -Port $ApiPort -TimeoutSec 30)) {
        throw "المنفذ $ApiPort ما زال مستخدماً بعد إيقاف API."
    }
    $portReleased = $true

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
        Write-DeployInfo "تم تخطي النسخة الاحتياطية للملفات (-SkipFileBackup). نسخة قاعدة البيانات تبقى إلزامية."
        $backupPath = "(متخطى)"
    }

    if ($requiredMigrationMissing) {
        Write-DeployStep "تطبيق migration المطلوبة تلقائيًا"
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
            -ExpectedLatestMigration $requiredMigration

        Test-RequiredMigrationApplied `
            -ConnectionString $sqlInfo.ConnectionString `
            -RequiredMigrationId $requiredMigration
        $databaseStatus = "نجح"
        $migrationsApplied = $true
    }

    Write-DeployStep "ترقية الإصدار عبر releases/current"
    $promotionStarted = $true
    $promotion = Install-StagedReleaseToProduction `
        -StagingPath $stagingPath `
        -InstallRoot $InstallRoot `
        -Version $packageVersion `
        -ConfigPath $ConfigPath `
        -PackagePath $PackagePath `
        -PackageCommit $packageCommit

    $configTarget = $promotion.ConfigTarget
    $releasePaths = $promotion.Paths
    $promotionCompleted = $true

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
        -ApiBindAddress $ApiBindAddress `
        -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
        -LogPath $logsPath

    Write-DeployStep "مصالحة Scheduled Task"
    $scheduledTaskReconcile = Sync-UqebApiScheduledTask `
        -TaskName $TaskName `
        -InstallRoot $InstallRoot
    $scheduledTaskBefore = $scheduledTaskReconcile.Before
    $scheduledTaskAfter = $scheduledTaskReconcile.After
    $scheduledTaskActionValid = [bool]$scheduledTaskReconcile.ActionValid
    $scheduledTaskReconcileStatus = if ($scheduledTaskReconcile.Created) {
        "تم الإنشاء"
    }
    elseif ($scheduledTaskReconcile.Updated) {
        "تم التصحيح"
    }
    else {
        "مطابق"
    }
    Write-DeployInfo ("ScheduledTask before: Execute='{0}', Arguments='{1}', WorkingDirectory='{2}'" -f `
            $scheduledTaskBefore.Execute, $scheduledTaskBefore.Arguments, $scheduledTaskBefore.WorkingDirectory)
    Write-DeployInfo ("ScheduledTask after: Execute='{0}', Arguments='{1}', WorkingDirectory='{2}', Valid='{3}'" -f `
            $scheduledTaskAfter.Execute, $scheduledTaskAfter.Arguments, $scheduledTaskAfter.WorkingDirectory, $scheduledTaskActionValid)

    if (-not $scheduledTaskActionValid) {
        throw "فشل تثبيت تعريف Scheduled Task '$TaskName': $($scheduledTaskReconcile.Reason)"
    }

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

    if ($databaseBackupPath) {
        $databaseRetentionDeleted = Invoke-DatabaseBackupRetentionPolicy `
            -BackupDirectory $databaseBackupDirectory `
            -InstallRoot $InstallRoot `
            -MinimumKeepCount 10 `
            -LatestSuccessfulBackupPath $databaseBackupPath
    }

    $deployedDir = Join-Path $InstallRoot "incoming\deployed"
    try {
        $archiveResult = Move-DeploymentPackageToArchive `
            -ZipPath $PackagePath `
            -Sha256Path $shaPath `
            -ArchiveDirectory $deployedDir
        $packageArchiveStatus = [string]$archiveResult.Status
    }
    catch {
        $packageArchiveStatus = "فشلت: $($_.Exception.Message)"
        Write-DeployFailure ("نجح النشر لكن فشلت أرشفة حزمة ZIP/SHA256: " + $_.Exception.Message)
    }

    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }

    $deploymentResult = "نجح"
}
catch {
    Write-DeployFailure ("فشل النشر: " + $_.Exception.Message)

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

    if ($promotionCompleted) {
        $rollbackHealthScript = Join-Path $ToolsRoot "verify-deployment-health.ps1"
        if (-not (Test-Path -LiteralPath $rollbackHealthScript)) {
            $rollbackHealthScript = Join-Path $stagingPath "scripts\verify-deployment-health.ps1"
        }

        $releaseRollback = $false
        try {
            $releaseRollback = Invoke-ReleaseRollbackFromState `
                -InstallRoot $InstallRoot `
                -TaskName $TaskName `
                -ApiPort $ApiPort `
                -ConfigPath $ConfigPath `
                -ReleaseManifestPath $releaseManifestPath `
                -ApiBaseUrl $ApiBaseUrl `
                -HealthScriptPath $rollbackHealthScript `
                -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
                -ExpectedBrowserExecutableSha256 ([string]$manifest.playwright.browserExecutableSha256) `
                -SkipPlaywrightProcessSmokeTest `
                -RequireHealthVerification
        }
        catch {
            Write-DeployFailure ("فشل التحقق من الإصدار السابق بعد rollback: " + $_.Exception.Message)
        }
        if ($releaseRollback) {
            $rollbackPerformed = $true
            Write-DeployInfo "تم استرجاع الإصدار السابق والتحقق من تشغيله بنجاح."
        }

        if (-not $rollbackPerformed -and -not $SkipFileBackup -and $backupPath -and $backupPath -ne "(متخطى)") {
            $filesRestored = Invoke-DeploymentFileRollback `
                -BackupApi (Join-Path $backupPath "api") `
                -BackupWeb (Join-Path $backupPath "web") `
                -ApiTarget $currentApiPath `
                -WebTarget (Join-Path $InstallRoot "current\web") `
                -ConfigTarget $configTarget `
                -ConfigSource $ConfigPath `
                -BackupBrowsers (Join-Path $backupPath "browsers") `
                -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
                -ReleaseManifestPath $releaseManifestPath `
                -BackupReleaseManifest (Join-Path $backupPath "release-manifest.json")

            if ($filesRestored) {
                try {
                    Sync-PublishCompatibilityLinks -InstallRoot $InstallRoot
                    Invoke-RestartCurrentReleaseService `
                        -TaskName $TaskName `
                        -ApiPort $ApiPort `
                        -ApiBaseUrl $ApiBaseUrl `
                        -HealthScriptPath $rollbackHealthScript `
                        -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
                        -ExpectedBrowserExecutableSha256 ([string]$manifest.playwright.browserExecutableSha256) `
                        -SkipPlaywrightProcessSmokeTest `
                        -RequireHealthVerification
                    $rollbackPerformed = $true
                }
                catch {
                    $rollbackPerformed = $false
                    Write-DeployFailure ("فشل التحقق بعد file rollback ويتطلب تدخلًا يدويًا: " + $_.Exception.Message)
                }
                if ($rollbackPerformed) {
                    Write-DeployInfo "تم استرجاع ملفات API/Web/Chromium والتحقق من صحة الإصدار. لا يوجد rollback تلقائي لقاعدة البيانات."
                }
            }
        }
    }
    elseif ($promotionStarted -and -not $promotionCompleted) {
        Write-DeployInfo "فشل أثناء الترقية؛ تم استرجاع current محليًا قبل إعادة تشغيل الخدمة."
        try {
            $healthScript = Join-Path $ToolsRoot "verify-deployment-health.ps1"
            Invoke-RestartCurrentReleaseService `
                -TaskName $TaskName `
                -ApiPort $ApiPort `
                -ApiBaseUrl $ApiBaseUrl `
                -HealthScriptPath $healthScript `
                -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
                -SkipPlaywrightProcessSmokeTest `
                -RequireHealthVerification
        }
        catch {
            Write-DeployFailure ("تعذر إعادة تشغيل API بعد فشل الترقية: " + $_.Exception.Message)
        }
    }
    elseif ($scheduledTaskStopped) {
        Write-DeployInfo "فشل بعد إيقاف API وقبل اكتمال الترقية؛ إعادة تشغيل الإصدار الحالي."
        try {
            $healthScript = Join-Path $ToolsRoot "verify-deployment-health.ps1"
            Invoke-RestartCurrentReleaseService `
                -TaskName $TaskName `
                -ApiPort $ApiPort `
                -ApiBaseUrl $ApiBaseUrl `
                -HealthScriptPath $healthScript `
                -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
                -SkipPlaywrightProcessSmokeTest `
                -RequireHealthVerification
        }
        catch {
            Write-DeployFailure ("تعذر إعادة تشغيل API بعد الفشل: " + $_.Exception.Message)
        }
    }
    else {
        Write-DeployInfo "فشل قبل إيقاف API؛ لا rollback للملفات ولا إعادة تشغيل."
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
    Write-DeployInfo ("حالة أرشفة الحزمة: " + $packageArchiveStatus)
    Write-DeployInfo ("حالة Scheduled Task: " + $scheduledTaskReconcileStatus)
    if ($scheduledTaskBefore) {
        Write-DeployInfo ("ScheduledTaskBeforeExecutable: " + $scheduledTaskBefore.Execute)
        Write-DeployInfo ("ScheduledTaskBeforeArguments: " + $scheduledTaskBefore.Arguments)
        Write-DeployInfo ("ScheduledTaskBeforeWorkingDirectory: " + $scheduledTaskBefore.WorkingDirectory)
    }
    if ($scheduledTaskAfter) {
        Write-DeployInfo ("ScheduledTaskExecutable: " + $scheduledTaskAfter.Execute)
        Write-DeployInfo ("ScheduledTaskArguments: " + $scheduledTaskAfter.Arguments)
        Write-DeployInfo ("ScheduledTaskWorkingDirectory: " + $scheduledTaskAfter.WorkingDirectory)
    }
    Write-DeployInfo ("ScheduledTaskActionValid: " + $scheduledTaskActionValid.ToString().ToLowerInvariant())
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
