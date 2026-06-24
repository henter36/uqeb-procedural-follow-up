#Requires -Version 5.1
<#
.SYNOPSIS
  بناء حزمة إنتاج offline جاهزة للنقل إلى جهاز الإنتاج.
#>

[CmdletBinding()]
param(
    [string]$ProductionApiBaseUrl = "http://10.0.177.17:5000/api",
    [string]$OutputRoot = "artifacts\production",
    [switch]$SkipPlaywrightBrowserDownload,
    [string]$PlaywrightBrowsersSourcePath = "",
    [ValidateSet("chromium")]
    [string]$PlaywrightBrowserName = "chromium",
    [switch]$SkipBackendTests,
    [switch]$SkipFrontendTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
. $commonPath

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Assert-RepositoryRoot -Root $repoRoot
Set-Location $repoRoot

$backendProject = Join-Path $repoRoot "backend\Uqeb.Api\Uqeb.Api.csproj"
$backendTests = Join-Path $repoRoot "backend\Uqeb.Api.Tests\Uqeb.Api.Tests.csproj"
$frontendRoot = Join-Path $repoRoot "frontend\uqeb-ui"
$migrationsDir = Join-Path $repoRoot "backend\Uqeb.Api\Migrations"
$artifactsRoot = Join-Path $repoRoot $OutputRoot
$tempStagingRoot = Join-Path $repoRoot "artifacts\production-staging-temp"

$testResults = [ordered]@{
    Backend = "لم يُنفَّذ"
    Frontend = "لم يُنفَّذ"
}

function Invoke-ExternalCommand {
    param(
        [string]$Label,
        [scriptblock]$Command
    )

    Write-DeployStep $Label
    & $Command
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "فشل الأمر '$Label' برمز الخروج: $LASTEXITCODE"
    }
}

function Ensure-DotNetEfToolAvailable {
    & dotnet ef --version *> $null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    Write-DeployInfo "تثبيت dotnet-ef global tool..."
    dotnet tool install --global dotnet-ef
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-ef غير متوفر ولم ينجح التثبيت."
    }
}

Write-DeployStep "التحقق من المتطلبات"
foreach ($tool in @("dotnet", "node", "npm", "powershell")) {
    if (-not (Test-CommandAvailable $tool)) {
        throw "الأداة المطلوبة غير متوفرة: $tool"
    }
    Write-DeployInfo "تم العثور على $tool"
}

Ensure-DotNetEfToolAvailable

$playwrightVersion = Get-PlaywrightPackageVersionFromProject -ProjectPath $backendProject
Write-DeployInfo ("إصدار Microsoft.Playwright: " + $playwrightVersion)

$drive = (Get-Item -LiteralPath $repoRoot).PSDrive
if ($drive.Free -and $drive.Free -lt 2GB) {
    throw "مساحة القرص المتاحة منخفضة لبناء الحزمة (أقل من 2GB)."
}

if ($SkipPlaywrightBrowserDownload) {
    if ([string]::IsNullOrWhiteSpace($PlaywrightBrowsersSourcePath)) {
        throw "يجب تحديد PlaywrightBrowsersSourcePath عند تخطي تنزيل Chromium."
    }
    Write-DeployInfo ("سيتم نسخ Chromium من المصدر: " + $PlaywrightBrowsersSourcePath)
}
else {
    Write-DeployInfo "سيتم تنزيل Chromium أثناء البناء (يتطلب الإنترنت على جهاز البناء)."
}

if (-not (Test-Path -LiteralPath $backendTests)) {
    throw "مشروع اختبارات Backend غير موجود: $backendTests"
}

if (-not $SkipBackendTests) {
    Invoke-ExternalCommand "تشغيل اختبارات Backend" {
        dotnet test $backendTests -c Release
    }
    $testResults.Backend = "نجح"
}
else {
    $testResults.Backend = "متخطى"
}

Push-Location $frontendRoot
try {
    if (-not $SkipFrontendTests) {
        Invoke-ExternalCommand "تثبيت اعتمادات Frontend" {
            npm ci
        }
        Invoke-ExternalCommand "تشغيل اختبارات Frontend" {
            npm test
        }
        $testResults.Frontend = "نجح"
    }
    else {
        $testResults.Frontend = "متخطى"
        if (-not (Test-Path -LiteralPath (Join-Path $frontendRoot "node_modules"))) {
            Invoke-ExternalCommand "تثبيت اعتمادات Frontend" {
                npm ci
            }
        }
    }

    Invoke-ExternalCommand "بناء Frontend للإنتاج" {
        $env:VITE_API_BASE_URL = $ProductionApiBaseUrl
        npm run build
    }
}
finally {
    Pop-Location
}

$publishDir = Join-Path $repoRoot "artifacts\publish-api"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Invoke-ExternalCommand "نشر Backend (Release)" {
    dotnet publish $backendProject -c Release -o $publishDir
}

$playwrightScript = Join-Path $publishDir "playwright.ps1"
if (-not (Test-Path -LiteralPath $playwrightScript)) {
    throw "ملف Playwright غير موجود بعد نشر API: $playwrightScript"
}

$migrationSqlPath = Join-Path $repoRoot "artifacts\migrations-idempotent.sql"
if (Test-Path -LiteralPath $migrationSqlPath) {
    Remove-Item -LiteralPath $migrationSqlPath -Force
}

Invoke-ExternalCommand "توليد سكربت EF Core idempotent migrations" {
    $env:ConnectionStrings__DefaultConnection =
        "Server=(localdb)\mssqllocaldb;Database=UqebPackageBuild;Trusted_Connection=True;TrustServerCertificate=True"
    dotnet ef migrations script `
        --idempotent `
        --project $backendProject `
        --output $migrationSqlPath
}

$migrationSql = Get-Content -LiteralPath $migrationSqlPath -Raw
$migrationSql = Repair-IdempotentMigrationScript -Content $migrationSql
if (-not (Test-IdempotentMigrationScriptRepaired -Content $migrationSql)) {
    throw "فشل إصلاح سكربت migrations: لا يوجد فصل GO بعد أعمدة NameNormalized."
}
Set-Content -LiteralPath $migrationSqlPath -Value $migrationSql -Encoding UTF8

if (Test-Path -LiteralPath $tempStagingRoot) {
    Remove-Item -LiteralPath $tempStagingRoot -Recurse -Force
}
Ensure-Directory $tempStagingRoot

$stagingApi = Join-Path $tempStagingRoot "api"
$stagingWeb = Join-Path $tempStagingRoot "web"
$stagingDatabase = Join-Path $tempStagingRoot "database"
$stagingScripts = Join-Path $tempStagingRoot "scripts"
$stagingBrowsers = Join-Path $tempStagingRoot "browsers"

Ensure-Directory $stagingApi
Ensure-Directory $stagingWeb
Ensure-Directory $stagingDatabase
Ensure-Directory $stagingScripts
Ensure-Directory $stagingBrowsers

$browserPayloadIncluded = $false

try {
    Write-DeployStep "تجهيز محتوى الحزمة"
    Invoke-RobocopySafe -Source $publishDir -Destination $stagingApi -TargetType Api -ExtraArguments @(
        "/XF", "appsettings.json", "appsettings.Development.json", "appsettings.Production.json"
    )

    $distPath = Join-Path $frontendRoot "dist"
    if (-not (Test-Path -LiteralPath (Join-Path $distPath "index.html"))) {
        throw "مخرجات بناء الواجهة غير موجودة."
    }
    Invoke-RobocopySafe -Source $distPath -Destination $stagingWeb -TargetType Web

    Copy-Item -LiteralPath $migrationSqlPath -Destination (Join-Path $stagingDatabase "migrations-idempotent.sql") -Force
    Ensure-Directory (Join-Path $stagingScripts "deployment")
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "apply-migrations.ps1") -Destination (Join-Path $stagingScripts "apply-migrations.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "verify-deployment-health.ps1") -Destination (Join-Path $stagingScripts "verify-deployment-health.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "verify-playwright-readiness.ps1") -Destination (Join-Path $stagingScripts "verify-playwright-readiness.ps1") -Force
    Copy-Item -LiteralPath $commonPath -Destination (Join-Path $stagingScripts "deployment\Common.ps1") -Force

    $browserExecutable = $null
    if ($SkipPlaywrightBrowserDownload) {
        Write-DeployInfo "تم تخطي تنزيل Chromium (-SkipPlaywrightBrowserDownload)."
        $sourcePath = [System.IO.Path]::GetFullPath($PlaywrightBrowsersSourcePath)
        Assert-PlaywrightBrowserSourcePathSafe `
            -SourcePath $sourcePath `
            -StagingBrowsersPath $stagingBrowsers `
            -TempStagingRoot $tempStagingRoot

        if (-not (Test-DirectoryHasContent $sourcePath)) {
            throw "مجلد Chromium المصدر غير موجود أو فارغ: $sourcePath"
        }

        Invoke-RobocopySafe `
            -Source $sourcePath `
            -Destination $stagingBrowsers `
            -TargetType Generic

        $browserExecutable = Get-PlaywrightBrowserExecutable -BrowsersRoot $stagingBrowsers
        $browserPayloadIncluded = $true
    }
    else {
        Write-DeployStep "تنزيل Chromium المتوافق مع Playwright"
        $browserExecutable = Invoke-PlaywrightChromiumInstall `
            -PlaywrightScriptPath $playwrightScript `
            -BrowsersRoot $stagingBrowsers
        Write-DeployInfo ("Chromium executable: " + $browserExecutable.RelativePath)
        $browserPayloadIncluded = $true
    }

    $browserManifest = New-PlaywrightBrowserManifest `
        -BrowsersRoot $stagingBrowsers `
        -PlaywrightScriptPath (Join-Path $stagingApi "playwright.ps1")
    $browserManifestPath = Join-Path $stagingBrowsers "playwright-browser-manifest.json"
    ($browserManifest | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $browserManifestPath -Encoding UTF8

    $versionStamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $commitSha = ""
    try {
        $commitSha = (git -C $repoRoot rev-parse HEAD 2>$null).Trim()
    }
    catch {
        $commitSha = ""
    }

    $minimumMigration = Get-LatestMigrationId -MigrationsDirectory $migrationsDir
    $browserRelativePath = 'browsers\' + ($browserExecutable.RelativePath -replace '/', '\')
    $manifestFiles = [ordered]@{}
    $importantFiles = @(
        "api\Uqeb.Api.dll",
        "api\playwright.ps1",
        "web\index.html",
        "database\migrations-idempotent.sql",
        "scripts\apply-migrations.ps1",
        "scripts\verify-deployment-health.ps1",
        "browsers\playwright-browser-manifest.json",
        $browserRelativePath
    )

    foreach ($relative in $importantFiles) {
        $full = Join-Path $tempStagingRoot $relative
        if (-not (Test-Path -LiteralPath $full)) {
            throw "ملف أساسي مفقود أثناء التجهيز: $relative"
        }
        $manifestFiles[$relative.Replace('\', '/')] = Get-FileSha256Hex -Path $full
    }

    $manifest = [ordered]@{
        applicationName = "Uqeb"
        version = $versionStamp
        buildTimestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        commitSha = $commitSha
        minimumDatabaseMigration = $minimumMigration
        playwright = [ordered]@{
            required = $true
            browser = $PlaywrightBrowserName
            browserPayloadIncluded = $browserPayloadIncluded
            packageVersion = $playwrightVersion
            browserRoot = "browsers"
            browserExecutableRelativePath = $browserExecutable.RelativePath
            browserExecutableSha256 = [string]$browserManifest.browserExecutableSha256
            playwrightScriptSha256 = [string]$browserManifest.playwrightScriptSha256
        }
        files = $manifestFiles
    }

    $manifestPath = Join-Path $tempStagingRoot "manifest.json"
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $manifestPath -Value $manifestJson -Encoding UTF8

    Ensure-Directory $artifactsRoot
    $zipName = "Uqeb-$versionStamp.zip"
    $zipPath = Join-Path $artifactsRoot $zipName
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Write-DeployStep "ضغط الحزمة"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempStagingRoot, $zipPath)

    $zipHash = Get-FileSha256Hex -Path $zipPath
    $shaPath = Join-Path $artifactsRoot ("Uqeb-$versionStamp.sha256.txt")
    Set-Content -LiteralPath $shaPath -Value "$zipHash  $zipName" -Encoding UTF8

    Write-DeployStep "اكتمل بناء حزمة الإنتاج"
    Write-DeployInfo ("مسار ZIP: " + $zipPath)
    Write-DeployInfo ("مسار SHA256: " + $shaPath)
    Write-DeployInfo ("حجم ZIP (بايت): " + (Get-Item -LiteralPath $zipPath).Length)
    Write-DeployInfo ("الإصدار: " + $versionStamp)
    Write-DeployInfo ("Commit SHA: " + ($(if ($commitSha) { $commitSha } else { "غير متوفر" })))
    Write-DeployInfo ("اختبار Backend: " + $testResults.Backend)
    Write-DeployInfo ("اختبار Frontend: " + $testResults.Frontend)
    Write-DeployInfo ("أدنى migration: " + $minimumMigration)
    Write-DeployInfo ("Chromium relative path: " + $browserExecutable.RelativePath)
}
finally {
    if (Test-Path -LiteralPath $tempStagingRoot) {
        Remove-Item -LiteralPath $tempStagingRoot -Recurse -Force
    }
}
