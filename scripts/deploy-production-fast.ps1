#Requires -Version 5.1
<#
.SYNOPSIS
  نشر سريع وآمن من مجلد النقل إلى الإنتاج مع تقرير GO/NO-GO.

.DESCRIPTION
  يعمل على جهاز الإنتاج. لا يحتاج وجود المستودع.
  يجب تشغيله كـ Administrator.

  الخطوات:
    1. ينسخ أدوات النشر إلى C:\UqebTools
    2. ينسخ الحزمة إلى C:\Uqeb\incoming
    3. يتحقق من SHA256
    4. يستدعي install-production-package.ps1
    5. يتحقق من: manifest ، IIS ، Scheduled Task ، شعار الخطاب ، health
    6. يطبع تقرير GO/NO-GO شامل

.EXAMPLE
  # من داخل مجلد النقل:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\deploy-production-fast.ps1 -TransferDir .

.EXAMPLE
  # من C:\UqebTools (بعد نسخ الأدوات يدوياً):
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\UqebTools\deploy-production-fast.ps1
#>

[CmdletBinding()]
param(
    [string]$TransferDir = "",

    [string]$InstallRoot         = "C:\Uqeb",
    [string]$ToolsRoot           = "C:\UqebTools",
    [string]$ConfigPath          = "C:\Uqeb\config\appsettings.Production.json",
    [string]$ApiPath             = "C:\Uqeb\publish\api",
    [string]$WebPath             = "C:\Uqeb\publish\web",
    [string]$TaskName            = "UqebApi",
    [int]   $ApiPort             = 5000,
    [string]$ApiBindAddress      = "10.0.177.17",
    [string]$PlaywrightBrowsersPath = "C:\Uqeb\tools\ms-playwright",

    [switch]$SkipFileBackup,
    [switch]$SkipHealthCheck,
    [switch]$SkipLogoCheck
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Collect report fields (all printed in final GO/NO-GO table)
# ---------------------------------------------------------------------------
$report = [ordered]@{
    "Package"                       = ""
    "Version"                       = ""
    "CommitSha"                     = ""
    "DatabaseCurrentMigration"      = ""
    "DatabaseRequiredMigration"     = ""
    "DatabaseUpgradeRequired"       = ""
    "DatabaseBackup"                = ""
    "DatabaseMigrationResult"       = ""
    "WebPath"                       = $WebPath
    "ApiPath"                       = $ApiPath
    "IISPhysicalPath"               = ""
    "ScheduledTaskExecutable"       = ""
    "ScheduledTaskArguments"        = ""
    "ScheduledTaskWorkingDirectory" = ""
    "ScheduledTaskActionValid"      = ""
    "ReleaseManifest"               = ""
    "LogoPath"                      = (Join-Path $ApiPath "Assets\Brand\organization-logo.png")
    "LogoExists"                    = ""
    "LogoLoadResult"                = ""
    "HealthLive"                    = ""
    "HealthReady"                   = ""
    "HealthOverall"                 = ""
    "LoginSmokeTest"                = "not_applicable"
    "FollowUpLetterPreviewLogo"     = "not_applicable (logo file existence and API endpoint verified; in-template rendering requires a browser session)"
    "FollowUpLetterPdfLogo"         = "not_applicable (logo file existence and API endpoint verified; PDF rendering requires headless browser)"
    "Result"                        = "NO-GO"
    "Reason"                        = "لم يكتمل النشر"
}

function Show-DeploymentReport {
    Write-Host ""
    Write-Host "======================================== DEPLOYMENT REPORT ========================================"
    foreach ($key in $report.Keys) {
        $val = if ($null -eq $report[$key]) { "" } else { [string]$report[$key] }
        Write-Host ("{0,-42}: {1}" -f $key, $val)
    }
    Write-Host "==================================================================================================="
    Write-Host ""
}

function Fail-Deploy {
    param([string]$Reason)
    $report["Result"] = "NO-GO"
    $report["Reason"] = $Reason
    Show-DeploymentReport
    exit 1
}

# ---------------------------------------------------------------------------
# Step 0: Verify running as Administrator
# ---------------------------------------------------------------------------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $report["Reason"] = "يجب تشغيل السكربت كـ Administrator."
    Show-DeploymentReport
    exit 1
}

# ---------------------------------------------------------------------------
# Step 1: Locate and copy tools to C:\UqebTools
# ---------------------------------------------------------------------------
$scriptSelf = $MyInvocation.MyCommand.Path
$scriptDir  = Split-Path -Parent $scriptSelf

# Determine transfer directory (where incoming\ and tools\ live)
if ([string]::IsNullOrWhiteSpace($TransferDir)) {
    # Try parent of this script (tools\ inside transfer package)
    $candidate = Split-Path -Parent $scriptDir
    if (Test-Path -LiteralPath (Join-Path $candidate "incoming")) {
        $TransferDir = $candidate
    }
    else {
        $TransferDir = $scriptDir
    }
}
$TransferDir = [System.IO.Path]::GetFullPath($TransferDir)

Write-Host "=== Uqeb Production Fast Deploy ===" -ForegroundColor Cyan
Write-Host "TransferDir : $TransferDir"
Write-Host "ToolsRoot   : $ToolsRoot"
Write-Host "InstallRoot : $InstallRoot"

# Create required directories
foreach ($dir in @(
    $InstallRoot,
    (Join-Path $InstallRoot "incoming"),
    $ToolsRoot,
    (Join-Path $ToolsRoot "deployment")
)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# Copy tools from transfer package to C:\UqebTools
$toolsSource = Join-Path $TransferDir "tools"
if (-not (Test-Path -LiteralPath $toolsSource)) {
    $toolsSource = $scriptDir  # already in tools dir
}

$requiredTools = @(
    "install-production-package.ps1",
    "apply-migrations.ps1",
    "verify-deployment-health.ps1",
    @{ Name = "deployment\Common.ps1"; Optional = $false }
)
foreach ($tool in $requiredTools) {
    $toolName = if ($tool -is [hashtable]) { $tool.Name } else { $tool }
    $isOptional = if ($tool -is [hashtable]) { $tool.Optional } else { $false }
    $src = Join-Path $toolsSource $toolName
    $dst = Join-Path $ToolsRoot $toolName
    if (Test-Path -LiteralPath $src) {
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item -LiteralPath $src -Destination $dst -Force
    }
    elseif (-not $isOptional) {
        Fail-Deploy "السكربت المطلوب غير موجود في مجلد الأدوات: $toolName"
    }
}

$verifyReadiness = Join-Path $toolsSource "verify-playwright-readiness.ps1"
if (Test-Path -LiteralPath $verifyReadiness) {
    Copy-Item -LiteralPath $verifyReadiness -Destination (Join-Path $ToolsRoot "verify-playwright-readiness.ps1") -Force
}

# Load Common.ps1 for utility functions
$commonPath = Join-Path $ToolsRoot "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    $commonPath = Join-Path $toolsSource "deployment\Common.ps1"
}
if (-not (Test-Path -LiteralPath $commonPath)) {
    Fail-Deploy "deployment\Common.ps1 غير موجود."
}
. $commonPath

# ---------------------------------------------------------------------------
# Step 2: Locate the ZIP package
# ---------------------------------------------------------------------------
Write-DeployStep "تحديد الحزمة"

$incomingDir = Join-Path $TransferDir "incoming"
if (-not (Test-Path -LiteralPath $incomingDir)) {
    $incomingDir = Join-Path $InstallRoot "incoming"
}

$packageZip  = $null
$packageSha  = $null

# Check transfer incoming first, then C:\Uqeb\incoming
foreach ($searchDir in @($incomingDir, (Join-Path $InstallRoot "incoming"))) {
    if (-not (Test-Path -LiteralPath $searchDir)) { continue }
    $zips = @(Get-ChildItem -LiteralPath $searchDir -Filter "Uqeb-*.zip" -File |
        Sort-Object LastWriteTime -Descending)
    if ($zips.Count -gt 0) {
        $packageZip = $zips[0].FullName
        try {
            $packageSha = Find-Sha256SidecarPath -ZipPath $packageZip
        }
        catch { $packageSha = $null }
        break
    }
}

if ([string]::IsNullOrWhiteSpace($packageZip) -or -not (Test-Path -LiteralPath $packageZip)) {
    Fail-Deploy "لم يتم العثور على حزمة Uqeb-*.zip في مجلد incoming."
}

$report["Package"] = $packageZip
Write-DeployInfo ("الحزمة: " + $packageZip)

# ---------------------------------------------------------------------------
# Step 3: Copy package to C:\Uqeb\incoming (if not already there)
# ---------------------------------------------------------------------------
$productionIncoming = Join-Path $InstallRoot "incoming"
$destZip = Join-Path $productionIncoming ([System.IO.Path]::GetFileName($packageZip))
if ($packageZip -ne $destZip) {
    Write-DeployStep "نسخ الحزمة إلى C:\Uqeb\incoming"
    Copy-Item -LiteralPath $packageZip -Destination $destZip -Force
    if ($packageSha -and (Test-Path -LiteralPath $packageSha)) {
        $destSha = Join-Path $productionIncoming ([System.IO.Path]::GetFileName($packageSha))
        Copy-Item -LiteralPath $packageSha -Destination $destSha -Force
        $packageSha = $destSha
    }
    $packageZip = $destZip
    $report["Package"] = $packageZip
}

# ---------------------------------------------------------------------------
# Step 4: Read manifest for version/migration info
# ---------------------------------------------------------------------------
Write-DeployStep "قراءة manifest الحزمة"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipArchive = $null
try {
    try {
        $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($packageZip)
    }
    catch {
        Fail-Deploy "تعذر فتح حزمة ZIP: $($_.Exception.Message)"
    }

    $me = $zipArchive.Entries | Where-Object { $_.FullName -eq 'manifest.json' } | Select-Object -First 1
    if (-not $me) {
        Fail-Deploy "manifest.json غير موجود في حزمة ZIP: $packageZip. الحزمة ربما تالفة أو بُنيت بإصدار قديم."
    }
    $ms = $me.Open()
    $mr = New-Object System.IO.StreamReader $ms
    $mj = $null
    try {
        $mj = $mr.ReadToEnd() | ConvertFrom-Json
    }
    catch {
        Fail-Deploy "تعذر تفسير manifest.json كـ JSON: $($_.Exception.Message)"
    }
    finally { $mr.Dispose(); $ms.Dispose() }

    $report["Version"]                   = [string]$mj.version
    $report["CommitSha"]                 = [string]$mj.commitSha
    $report["DatabaseRequiredMigration"] = [string]$mj.minimumDatabaseMigration

    if ([string]::IsNullOrWhiteSpace($report["Version"])) {
        Fail-Deploy "حقل 'version' مفقود في manifest.json. الحزمة غير صالحة."
    }
    if ([string]::IsNullOrWhiteSpace($report["DatabaseRequiredMigration"])) {
        Fail-Deploy "حقل 'minimumDatabaseMigration' مفقود في manifest.json. الحزمة غير صالحة."
    }
}
finally {
    if ($null -ne $zipArchive) { $zipArchive.Dispose() }
}

Write-DeployInfo ("الإصدار: " + $report["Version"])
Write-DeployInfo ("آخر migration مطلوبة: " + $report["DatabaseRequiredMigration"])

# ---------------------------------------------------------------------------
# Step 5: Pre-flight — DB current migration
# ---------------------------------------------------------------------------
if (Test-Path -LiteralPath $ConfigPath) {
    try {
        $sqlInfo = Get-SqlConnectionInfoFromSettings -SettingsPath $ConfigPath
        $currentMigration = Invoke-SqlDeploymentCommand `
            -Server $sqlInfo.Server `
            -Database $sqlInfo.Database `
            -ConnectionString $sqlInfo.ConnectionString `
            -CommandText "IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL SELECT NULL ELSE SELECT TOP (1) [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC" `
            -Scalar
        $report["DatabaseCurrentMigration"] = [string]$currentMigration
    }
    catch {
        $report["DatabaseCurrentMigration"] = "error: $($_.Exception.Message)"
    }

    $required = $report["DatabaseRequiredMigration"]
    $current  = $report["DatabaseCurrentMigration"]
    if (-not [string]::IsNullOrWhiteSpace($required) -and
        -not [string]::IsNullOrWhiteSpace($current) -and
        $current -ge $required) {
        $report["DatabaseUpgradeRequired"] = "false"
    }
    else {
        $report["DatabaseUpgradeRequired"] = "true"
        Write-DeployInfo "Database upgrade required"
    }
}
else {
    Write-Warning "appsettings.Production.json غير موجود. لن يتم التحقق من حالة DB قبل التثبيت."
    $report["DatabaseCurrentMigration"] = "unknown (config missing)"
    $report["DatabaseUpgradeRequired"]  = "unknown"
}

# ---------------------------------------------------------------------------
# Step 6: Invoke install-production-package.ps1
# ---------------------------------------------------------------------------
Write-DeployStep "استدعاء install-production-package.ps1"

$installScript = Join-Path $ToolsRoot "install-production-package.ps1"
if (-not (Test-Path -LiteralPath $installScript)) {
    Fail-Deploy "install-production-package.ps1 غير موجود في $ToolsRoot"
}

$installArgs = @{
    PackagePath          = $packageZip
    ApiPath              = $ApiPath
    WebPath              = $WebPath
    ConfigPath           = $ConfigPath
    TaskName             = $TaskName
    ApiPort              = $ApiPort
    ApiBindAddress       = $ApiBindAddress
    InstallRoot          = $InstallRoot
    ToolsRoot            = $ToolsRoot
    PlaywrightBrowsersPath = $PlaywrightBrowsersPath
}
if ($SkipFileBackup) { $installArgs["SkipFileBackup"] = $true }

try {
    & $installScript @installArgs
    $installExitCode = $LASTEXITCODE
}
catch {
    $report["DatabaseMigrationResult"] = "error during install: $($_.Exception.Message)"
    Fail-Deploy "فشل install-production-package.ps1: $($_.Exception.Message)"
}

if ($installExitCode -and $installExitCode -ne 0) {
    Fail-Deploy "install-production-package.ps1 خرج بكود $installExitCode. راجع الأخطاء أعلاه."
}

$report["DatabaseMigrationResult"] = "applied"

# ---------------------------------------------------------------------------
# Step 7: Post-install verification
# ---------------------------------------------------------------------------
Write-DeployStep "التحقق من نتيجة التثبيت"

$releaseManifestPath = Join-Path (Split-Path $ApiPath -Parent) "release-manifest.json"

# 7a. Verify expected files
foreach ($required in @(
    @{ Path = (Join-Path $WebPath "index.html");        Label = "web\index.html" },
    @{ Path = (Join-Path $ApiPath "Uqeb.Api.dll");      Label = "api\Uqeb.Api.dll" },
    @{ Path = $releaseManifestPath;                      Label = "release-manifest.json" }
)) {
    if (-not (Test-Path -LiteralPath $required.Path)) {
        Fail-Deploy ("ملف مطلوب غير موجود بعد التثبيت: " + $required.Label)
    }
}

$report["ReleaseManifest"] = $releaseManifestPath
Write-DeployInfo "release-manifest.json موجود."

# 7b. Verify release manifest contents match package
$releaseManifestJson = $null
try {
    $releaseManifestJson = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json
}
catch {
    Fail-Deploy "تعذر قراءة release-manifest.json أو تفسيره: $($_.Exception.Message)"
}
$manifestVersion = [string]$releaseManifestJson.version
if (-not [string]::IsNullOrWhiteSpace($report["Version"]) -and
    $manifestVersion -ne $report["Version"]) {
    Fail-Deploy ("إصدار manifest لا يطابق الحزمة. manifest=$manifestVersion, package=$($report['Version'])")
}

# 7c. Verify IIS physical path
try {
    Import-Module WebAdministration -ErrorAction SilentlyContinue
    $site = Get-WebSite | Where-Object { $_.PhysicalPath -like "*Uqeb*" } | Select-Object -First 1
    if ($site) {
        $report["IISPhysicalPath"] = $site.PhysicalPath
        $physicalNorm   = $site.PhysicalPath.TrimEnd('\').ToLower()
        $webNorm        = $WebPath.TrimEnd('\').ToLower()
        # Accept both the junction path (publish\web) and its target (current\web)
        $webCurrentNorm = (Join-Path $InstallRoot "current\web").ToLower().TrimEnd('\')
        if ($physicalNorm -ne $webNorm -and $physicalNorm -ne $webCurrentNorm) {
            Fail-Deploy ("IIS يعرض '$($site.PhysicalPath)' بينما المتوقع '$WebPath'. يجب تصحيح إعدادات IIS قبل الدمج.")
        }
    }
    else {
        $report["IISPhysicalPath"] = "لم يتم العثور على موقع IIS"
        Fail-Deploy "لا يوجد موقع IIS يشير إلى مسار Uqeb. تحقق من إعداد IIS على هذا الجهاز."
    }
}
catch {
    $report["IISPhysicalPath"] = "غير قابل للتحقق: $($_.Exception.Message)"
}

# 7d. Verify Scheduled Task
try {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        $actions = $task.Actions
        $exe = [string]($actions | Select-Object -First 1 -ExpandProperty Execute)
        $arguments = [string]($actions | Select-Object -First 1 -ExpandProperty Arguments)
        $wd  = [string]($actions | Select-Object -First 1 -ExpandProperty WorkingDirectory)
        $report["ScheduledTaskExecutable"]       = $exe
        $report["ScheduledTaskArguments"]        = $arguments
        $report["ScheduledTaskWorkingDirectory"] = $wd
        $validation = Test-UqebApiScheduledTaskAction `
            -Execute $exe `
            -Arguments $arguments `
            -WorkingDirectory $wd `
            -InstallRoot $InstallRoot
        $report["ScheduledTaskActionValid"] = ([bool]$validation.IsValid).ToString().ToLowerInvariant()

        if (-not $validation.IsValid) {
            Fail-Deploy ("ScheduledTask '$TaskName' غير مطابق: Execute='$exe', Arguments='$arguments', WorkingDirectory='$wd', Reason='$($validation.Reason)'. المتوقع Execute='$(Join-Path $InstallRoot "run-api.cmd")' و WorkingDirectory='$InstallRoot'.")
        }
    }
    else {
        $report["ScheduledTaskExecutable"]       = "Scheduled Task '$TaskName' غير موجود"
        $report["ScheduledTaskArguments"]        = "N/A"
        $report["ScheduledTaskWorkingDirectory"] = "N/A"
        $report["ScheduledTaskActionValid"]      = "false"
        Fail-Deploy "Scheduled Task '$TaskName' غير موجود. يجب إنشاؤه على جهاز الإنتاج قبل أول نشر."
    }
}
catch {
    $report["ScheduledTaskExecutable"]       = "error: $($_.Exception.Message)"
    $report["ScheduledTaskArguments"]        = "N/A"
    $report["ScheduledTaskWorkingDirectory"] = "N/A"
    $report["ScheduledTaskActionValid"]      = "false"
    Fail-Deploy "تعذر التحقق من Scheduled Task '$TaskName': $($_.Exception.Message)"
}

# 7e. Verify DB migration applied
if (Test-Path -LiteralPath $ConfigPath) {
    try {
        $sqlInfoPost = Get-SqlConnectionInfoFromSettings -SettingsPath $ConfigPath
        $postMigration = Invoke-SqlDeploymentCommand `
            -Server $sqlInfoPost.Server `
            -Database $sqlInfoPost.Database `
            -ConnectionString $sqlInfoPost.ConnectionString `
            -CommandText "IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL SELECT NULL ELSE SELECT TOP (1) [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC" `
            -Scalar
        $report["DatabaseCurrentMigration"] = [string]$postMigration

        $reqMig = $report["DatabaseRequiredMigration"]
        if (-not [string]::IsNullOrWhiteSpace($reqMig) -and
            ([string]::IsNullOrWhiteSpace($postMigration) -or $postMigration -lt $reqMig)) {
            Fail-Deploy ("آخر migration مطبّق ($postMigration) أقل من المطلوب ($reqMig).")
        }
        $report["DatabaseMigrationResult"] = "verified ($postMigration)"
    }
    catch {
        $report["DatabaseMigrationResult"] = "error: $($_.Exception.Message)"
    }
}

# 7f. Verify logo file
$logoPath = Join-Path $ApiPath "Assets\Brand\organization-logo.png"
$report["LogoPath"] = $logoPath
if (Test-Path -LiteralPath $logoPath) {
    $logoSize = (Get-Item -LiteralPath $logoPath).Length
    if ($logoSize -gt 0) {
        $report["LogoExists"]     = "true (${logoSize} bytes)"
        $report["LogoLoadResult"] = "ok"
    }
    else {
        $report["LogoExists"]     = "true (empty!)"
        $report["LogoLoadResult"] = "fail: file is empty"
        if (-not $SkipLogoCheck) {
            Fail-Deploy "ملف الشعار موجود لكنه فارغ: $logoPath"
        }
    }
}
else {
    $report["LogoExists"]     = "false"
    $report["LogoLoadResult"] = "fail: file not found"
    if (-not $SkipLogoCheck) {
        Fail-Deploy "ملف الشعار غير موجود: $logoPath. تحقق أن الحزمة تحتوي Assets\Brand\organization-logo.png."
    }
}

# 7g. Health checks
if (-not $SkipHealthCheck) {
    $ApiBaseUrl = "http://${ApiBindAddress}:${ApiPort}"
    Write-DeployStep "فحص health endpoints"

    function Get-HttpResponseStatusCode {
        # Safely extract HTTP status code from a caught exception under Set-StrictMode -Version Latest.
        # .Response exists on WebException (PS 5.1) and HttpResponseException (PS 7+) but not on all
        # exception types; accessing a missing .NET property under StrictMode throws, hence the inner try.
        param($ExceptionRecord)
        try {
            $resp = $ExceptionRecord.Exception.Response
            if ($null -ne $resp) { return [int]$resp.StatusCode }
        }
        catch { }
        return $null
    }

    function Invoke-SimpleGet {
        param([string]$Uri, [int]$TimeoutSec = 20)
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec $TimeoutSec -ErrorAction Stop
            return [pscustomobject]@{ StatusCode = [int]$r.StatusCode; Content = $r.Content; Error = $null }
        }
        catch {
            return [pscustomobject]@{
                StatusCode = Get-HttpResponseStatusCode $_
                Content    = $null
                Error      = $_.Exception.Message
            }
        }
    }

    function Invoke-SimplePost {
        param([string]$Uri, [string]$Body, [string]$ContentType = "application/json", [int]$TimeoutSec = 20)
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Method POST `
                -Body $Body -ContentType $ContentType -TimeoutSec $TimeoutSec -ErrorAction Stop
            return [pscustomobject]@{ StatusCode = [int]$r.StatusCode; Content = $r.Content; Error = $null }
        }
        catch {
            return [pscustomobject]@{
                StatusCode = Get-HttpResponseStatusCode $_
                Content    = $null
                Error      = $_.Exception.Message
            }
        }
    }

    # /health/live
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $live = Invoke-SimpleGet -Uri "$ApiBaseUrl/health/live"
        if ($live.StatusCode -eq 200) {
            $report["HealthLive"] = "200 OK"
            break
        }
        if ($attempt -lt 5) { Start-Sleep -Seconds 3 }
        else {
            $report["HealthLive"] = "fail ($($live.StatusCode)) $($live.Error)"
            Fail-Deploy "/health/live لم يُعِد 200 بعد 5 محاولات."
        }
    }

    # /health/ready — must be 200 for GO
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $ready = Invoke-SimpleGet -Uri "$ApiBaseUrl/health/ready"
        if ($ready.StatusCode -eq 200) {
            $report["HealthReady"] = "200 OK"
            break
        }
        if ($attempt -lt 5) { Start-Sleep -Seconds 3 }
        else {
            $report["HealthReady"] = "fail ($($ready.StatusCode)) $($ready.Error)"
            # Report check details from body
            if ($ready.Content) {
                try {
                    $rb = $ready.Content | ConvertFrom-Json
                    $checksVal = $rb.PSObject.Properties['checks']
                    if ($null -ne $checksVal -and $null -ne $checksVal.Value) {
                        $failedChecks = $checksVal.Value.PSObject.Properties |
                            Where-Object { $_.Value -notin @('pass', 'not_applicable') } |
                            ForEach-Object { "$($_.Name)=$($_.Value)" }
                        $report["HealthReady"] += " [failed: " + ($failedChecks -join ", ") + "]"
                    }
                }
                catch {
                    Write-Verbose "تعذر تفسير body الـ /health/ready: $($_.Exception.Message)"
                }
            }
            Fail-Deploy "/health/ready لم يُعِد 200. النظام غير جاهز بالكامل."
        }
    }

    # /health (summary)
    $overall = Invoke-SimpleGet -Uri "$ApiBaseUrl/health"
    if ($overall.StatusCode -eq 200) {
        $report["HealthOverall"] = "200 OK"
    }
    else {
        $report["HealthOverall"] = "fail ($($overall.StatusCode)) $($overall.Error)"
    }

    # Logo via API endpoint
    if ($report["LogoExists"] -like "true*") {
        $logoApi = Invoke-SimpleGet -Uri "$ApiBaseUrl/api/branding/organization-logo"
        if ($logoApi.StatusCode -eq 200) {
            $report["LogoLoadResult"] = "ok (API 200)"
        }
        else {
            $report["LogoLoadResult"] = "fail: API returned $($logoApi.StatusCode)"
            if (-not $SkipLogoCheck) {
                Fail-Deploy "الشعار موجود لكن API endpoint /api/branding/organization-logo أعاد $($logoApi.StatusCode)"
            }
        }
    }

    # Login smoke test — POST with obviously-invalid credentials, must get 401
    # A 200 would mean auth middleware is misconfigured; connection failure means API is not reachable.
    $loginProbe = Invoke-SimplePost -Uri "$ApiBaseUrl/api/auth/login" `
        -Body '{"username":"__deploy_probe__","password":"__deploy_probe__"}'
    if ($loginProbe.StatusCode -eq 401) {
        $report["LoginSmokeTest"] = "ok (401 as expected)"
    }
    elseif ($loginProbe.StatusCode -ge 200 -and $loginProbe.StatusCode -lt 300) {
        $report["LoginSmokeTest"] = "fail: got $($loginProbe.StatusCode) for invalid credentials (expected 401)"
        Fail-Deploy "LoginSmokeTest: أعاد $($loginProbe.StatusCode) لبيانات اعتماد خاطئة. المتوقع 401. تحقق من auth middleware."
    }
    else {
        $report["LoginSmokeTest"] = "fail ($($loginProbe.StatusCode)) $($loginProbe.Error)"
        Fail-Deploy "LoginSmokeTest: /api/auth/login فشل في الاستجابة. $($loginProbe.Error)"
    }
}

# ---------------------------------------------------------------------------
# Final GO
# ---------------------------------------------------------------------------
$report["Result"] = "GO"
$report["Reason"] = "اكتمل النشر وجميع الفحوصات نجحت."

Show-DeploymentReport
Write-Host "✓ Result: GO" -ForegroundColor Green
exit 0
