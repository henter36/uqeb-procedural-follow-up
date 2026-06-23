#Requires -Version 5.1
<#
.SYNOPSIS
  بناء حزمة إنتاج offline جاهزة للنقل إلى جهاز الإنتاج.
#>

[CmdletBinding()]
param(
    [string]$ProductionApiBaseUrl = "http://10.0.177.17:5000/api",
    [string]$OutputRoot = "artifacts\production"
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

Write-DeployStep "التحقق من المتطلبات"
foreach ($tool in @("dotnet", "node", "npm")) {
    if (-not (Test-CommandAvailable $tool)) {
        throw "الأداة المطلوبة غير متوفرة: $tool"
    }
    Write-DeployInfo "تم العثور على $tool"
}

if (-not (Test-Path -LiteralPath $backendTests)) {
    throw "مشروع اختبارات Backend غير موجود: $backendTests"
}

Invoke-ExternalCommand "تشغيل اختبارات Backend" {
    dotnet test $backendTests -c Release --no-restore:$false
}
$testResults.Backend = "نجح"

Push-Location $frontendRoot
try {
    Invoke-ExternalCommand "تشغيل اختبارات Frontend" {
        npm ci
        npm test
    }
    $testResults.Frontend = "نجح"

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

$migrationSqlPath = Join-Path $repoRoot "artifacts\migrations-idempotent.sql"
if (Test-Path -LiteralPath $migrationSqlPath) {
    Remove-Item -LiteralPath $migrationSqlPath -Force
}

Invoke-ExternalCommand "توليد سكربت EF Core idempotent migrations" {
    dotnet ef migrations script `
        --idempotent `
        --project $backendProject `
        --output $migrationSqlPath
}

$migrationSql = Get-Content -LiteralPath $migrationSqlPath -Raw
$migrationSql = Repair-IdempotentMigrationScript -Content $migrationSql
if ($migrationSql -notmatch '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*GO\s*UPDATE\s+Departments') {
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

Ensure-Directory $stagingApi
Ensure-Directory $stagingWeb
Ensure-Directory $stagingDatabase
Ensure-Directory $stagingScripts

Write-DeployStep "تجهيز محتوى الحزمة"
Invoke-RobocopyWithoutMirror -Source $publishDir -Destination $stagingApi -ExtraArguments @(
    "/XF", "appsettings.json", "appsettings.Development.json", "appsettings.Production.json"
)

$distPath = Join-Path $frontendRoot "dist"
if (-not (Test-Path -LiteralPath (Join-Path $distPath "index.html"))) {
    throw "مخرجات بناء الواجهة غير موجودة."
}
Invoke-RobocopyWithoutMirror -Source $distPath -Destination $stagingWeb

Copy-Item -LiteralPath $migrationSqlPath -Destination (Join-Path $stagingDatabase "migrations-idempotent.sql") -Force
Ensure-Directory (Join-Path $stagingScripts "deployment")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "apply-migrations.ps1") -Destination (Join-Path $stagingScripts "apply-migrations.ps1") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "verify-deployment-health.ps1") -Destination (Join-Path $stagingScripts "verify-deployment-health.ps1") -Force
Copy-Item -LiteralPath $commonPath -Destination (Join-Path $stagingScripts "deployment\Common.ps1") -Force

$versionStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$commitSha = ""
try {
    $commitSha = (git -C $repoRoot rev-parse HEAD 2>$null).Trim()
}
catch {
    $commitSha = ""
}

$minimumMigration = Get-LatestMigrationId -MigrationsDirectory $migrationsDir
$manifestFiles = [ordered]@{}
$importantFiles = @(
    "api\Uqeb.Api.dll",
    "web\index.html",
    "database\migrations-idempotent.sql",
    "scripts\apply-migrations.ps1",
    "scripts\verify-deployment-health.ps1"
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
    files = $manifestFiles
}

$manifestPath = Join-Path $tempStagingRoot "manifest.json"
$manifestJson = $manifest | ConvertTo-Json -Depth 6
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

Remove-Item -LiteralPath $tempStagingRoot -Recurse -Force

Write-DeployStep "اكتمل بناء حزمة الإنتاج"
Write-DeployInfo ("مسار ZIP: " + $zipPath)
Write-DeployInfo ("مسار SHA256: " + $shaPath)
Write-DeployInfo ("الإصدار: " + $versionStamp)
Write-DeployInfo ("Commit SHA: " + ($(if ($commitSha) { $commitSha } else { "غير متوفر" })))
Write-DeployInfo ("اختبار Backend: " + $testResults.Backend)
Write-DeployInfo ("اختبار Frontend: " + $testResults.Frontend)
Write-DeployInfo ("أدنى migration: " + $minimumMigration)
