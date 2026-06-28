#Requires -Version 5.1
<#
.SYNOPSIS
  تطبيق migrations-idempotent.sql على SQL Server بدون sqlcmd.
#>

[CmdletBinding()]
param(
    [string]$Server,
    [string]$Database,
    [string]$SettingsPath,
    [string]$ConnectionString,
    [Parameter(Mandatory = $true)]
    [string]$MigrationFile,
    [string]$ExpectedLatestMigration
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$commonCandidates = @(
    (Join-Path $PSScriptRoot "deployment\Common.ps1"),
    (Join-Path $PSScriptRoot "..\deployment\Common.ps1")
)
foreach ($candidate in $commonCandidates) {
    if (Test-Path -LiteralPath $candidate) {
        . $candidate
        break
    }
}

if (-not (Get-Command Split-SqlBatches -ErrorAction SilentlyContinue)) {
    throw "تعذر تحميل وحدات النشر المشتركة."
}

if (-not [string]::IsNullOrWhiteSpace($SettingsPath)) {
    $sqlInfo = Get-SqlConnectionInfoFromSettings -SettingsPath $SettingsPath
    $ConnectionString = $sqlInfo.ConnectionString
    $Server = $sqlInfo.Server
    $Database = $sqlInfo.Database
}
elseif ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "يجب تمرير SettingsPath أو ConnectionString."
}
else {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    if ([string]::IsNullOrWhiteSpace($Server)) { $Server = $builder.DataSource }
    if ([string]::IsNullOrWhiteSpace($Database)) { $Database = $builder.InitialCatalog }
}

if ([string]::IsNullOrWhiteSpace($Server) -or [string]::IsNullOrWhiteSpace($Database)) {
    throw "تعذر تحديد Server/Database من إعدادات الاتصال."
}

if (-not (Test-Path -LiteralPath $MigrationFile)) {
    throw "ملف migrations غير موجود: $MigrationFile"
}

# Disable MARS: a transaction that spans GO-separated batches must not be
# interrupted by an active result set on a parallel MARS channel.
$csBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
$csBuilder['MultipleActiveResultSets'] = $false
$ConnectionString = $csBuilder.ConnectionString

function New-MigrationConnection {
    return New-SqlDeploymentConnection -ConnectionString $ConnectionString -Database $Database
}

function Get-LatestAppliedMigration {
    $conn = New-MigrationConnection
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = @"
IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
    SELECT NULL
ELSE
    SELECT TOP (1) [MigrationId]
    FROM [__EFMigrationsHistory]
    ORDER BY [MigrationId] DESC
"@
        $result = $cmd.ExecuteScalar()
        return [string]$result
    }
    finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
        $conn.Dispose()
    }
}

function Test-TableColumnExists {
    param([string]$TableName, [string]$ColumnName)
    $conn = New-MigrationConnection
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
) THEN 1 ELSE 0 END
"@
        [void]$cmd.Parameters.AddWithValue("@tableName", $TableName)
        [void]$cmd.Parameters.AddWithValue("@columnName", $ColumnName)
        return ([int]$cmd.ExecuteScalar() -eq 1)
    }
    finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
        $conn.Dispose()
    }
}

function Test-TableExists {
    param([string]$TableName)
    $conn = New-MigrationConnection
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName
) THEN 1 ELSE 0 END
"@
        [void]$cmd.Parameters.AddWithValue("@tableName", $TableName)
        return ([int]$cmd.ExecuteScalar() -eq 1)
    }
    finally {
        if ($conn.State -eq 'Open') { $conn.Close() }
        $conn.Dispose()
    }
}

Write-DeployStep ("تطبيق migrations: " + (Get-SqlRedactedConnectionLabel -Server $Server -Database $Database))
Write-DeployInfo ("ملف migrations: " + $MigrationFile)

if (-not [string]::IsNullOrWhiteSpace($ExpectedLatestMigration)) {
    Write-DeployInfo ("آخر migration مطلوبة: " + $ExpectedLatestMigration)
}

$currentMigrationBefore = Get-LatestAppliedMigration
Write-DeployInfo ("آخر migration مطبّق حالياً: " + $(
    if ([string]::IsNullOrWhiteSpace($currentMigrationBefore)) { "(لا يوجد)" } else { $currentMigrationBefore }
))

# Skip if already up-to-date
if (-not [string]::IsNullOrWhiteSpace($ExpectedLatestMigration) -and
    -not [string]::IsNullOrWhiteSpace($currentMigrationBefore) -and
    [string]$currentMigrationBefore -ge [string]$ExpectedLatestMigration) {
    Write-DeployInfo "قاعدة البيانات محدّثة بالفعل. لا حاجة لتطبيق migrations."
    Write-DeployInfo "اكتمل التحقق بنجاح."
    exit 0
}

Write-DeployInfo "ترقية قاعدة البيانات مطلوبة."

$sql = Get-Content -LiteralPath $MigrationFile -Raw -Encoding UTF8

# Apply structural repairs before splitting (fixes known SQL Server batch-compile issues)
$sql = Repair-IdempotentMigrationScript -Content $sql

# Required SQL Server session settings. Without QUOTED_IDENTIFIER=ON, updating rows
# in tables that have filtered indexes or computed columns raises error 1934.
$setOptionsPreamble = @"
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
"@

$batches = Split-SqlBatches -SqlContent $sql
if ($batches.Count -eq 0) {
    throw "ملف migrations فارغ أو غير صالح."
}

$allBatches = [System.Collections.Generic.List[string]]::new()
$allBatches.Add($setOptionsPreamble)
foreach ($b in $batches) { $allBatches.Add($b) }

$connection = New-MigrationConnection

try {
    $connection.Open()
    Write-DeployInfo ("تنفيذ " + $allBatches.Count + " batch")

    $batchNumber = 0
    foreach ($batch in $allBatches) {
        $batchNumber++
        if ([string]::IsNullOrWhiteSpace($batch)) { continue }

        $command = $connection.CreateCommand()
        $command.CommandText = $batch
        $command.CommandTimeout = 0
        try {
            [void]$command.ExecuteNonQuery()
        }
        catch {
            $preview = ($batch -split "`r?`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -First 3) -join " | "
            throw "فشل تنفيذ batch رقم $batchNumber. بداية batch: $preview. رسالة SQL: $($_.Exception.Message)"
        }
    }
}
finally {
    if ($connection.State -eq 'Open') { $connection.Close() }
    $connection.Dispose()
}

Write-DeployStep "التحقق بعد تطبيق migrations"
$latestMigration = Get-LatestAppliedMigration
if ([string]::IsNullOrWhiteSpace($latestMigration)) {
    throw "جدول __EFMigrationsHistory غير موجود أو فارغ."
}

Write-DeployInfo ("آخر migration مطبّق: " + $latestMigration)

if (-not [string]::IsNullOrWhiteSpace($ExpectedLatestMigration) -and $latestMigration -lt $ExpectedLatestMigration) {
    throw "آخر migration مطبّق ($latestMigration) أقل من المتوقع ($ExpectedLatestMigration)."
}

foreach ($table in @("Departments", "ExternalParties", "Categories")) {
    if (-not (Test-TableColumnExists -TableName $table -ColumnName "NameNormalized")) {
        throw "العمود NameNormalized مفقود من الجدول $table."
    }
}

foreach ($table in @("LoginAttemptLogs", "SecurityAlerts")) {
    if (-not (Test-TableExists -TableName $table)) {
        throw "الجدول المطلوب مفقود: $table."
    }
}

Write-DeployInfo "اكتمل تطبيق migrations والتحقق بنجاح."
