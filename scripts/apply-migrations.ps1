#Requires -Version 5.1
<#
.SYNOPSIS
  تطبيق migrations-idempotent.sql على SQL Server بدون sqlcmd.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$Database,

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

if (-not (Test-Path -LiteralPath $MigrationFile)) {
    throw "ملف migrations غير موجود: $MigrationFile"
}

$sql = Get-Content -LiteralPath $MigrationFile -Raw
$batches = Split-SqlBatches -SqlContent $sql
if ($batches.Count -eq 0) {
    throw "ملف migrations فارغ أو غير صالح."
}

$connectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString

try {
    $connection.Open()
    Write-DeployInfo ("الاتصال بقاعدة البيانات: Server=$Server; Database=$Database")

    $batchNumber = 0
    foreach ($batch in $batches) {
        $batchNumber++
        if ([string]::IsNullOrWhiteSpace($batch)) {
            continue
        }

        $command = $connection.CreateCommand()
        $command.CommandText = $batch
        $command.CommandTimeout = 0
        try {
            [void]$command.ExecuteNonQuery()
        }
        catch {
            $preview = ($batch -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 3) -join " | "
            throw "فشل تنفيذ batch رقم $batchNumber. بداية batch: $preview. رسالة SQL: $($_.Exception.Message)"
        }
    }
}
finally {
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    $connection.Dispose()
}

function Test-TableColumnExists {
    param(
        [string]$TableName,
        [string]$ColumnName
    )

    $checkConnection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    try {
        $checkConnection.Open()
        $command = $checkConnection.CreateCommand()
        $command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
) THEN 1 ELSE 0 END
"@
        [void]$command.Parameters.AddWithValue("@tableName", $TableName)
        [void]$command.Parameters.AddWithValue("@columnName", $ColumnName)
        return ([int]$command.ExecuteScalar() -eq 1)
    }
    finally {
        if ($checkConnection.State -eq 'Open') {
            $checkConnection.Close()
        }
        $checkConnection.Dispose()
    }
}

function Test-TableExists {
    param([string]$TableName)

    $checkConnection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    try {
        $checkConnection.Open()
        $command = $checkConnection.CreateCommand()
        $command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName
) THEN 1 ELSE 0 END
"@
        [void]$command.Parameters.AddWithValue("@tableName", $TableName)
        return ([int]$command.ExecuteScalar() -eq 1)
    }
    finally {
        if ($checkConnection.State -eq 'Open') {
            $checkConnection.Close()
        }
        $checkConnection.Dispose()
    }
}

function Get-LatestAppliedMigration {
    $checkConnection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    try {
        $checkConnection.Open()
        $command = $checkConnection.CreateCommand()
        $command.CommandText = @"
IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
    SELECT NULL
ELSE
    SELECT TOP (1) [MigrationId]
    FROM [__EFMigrationsHistory]
    ORDER BY [MigrationId] DESC
"@
        $result = $command.ExecuteScalar()
        return [string]$result
    }
    finally {
        if ($checkConnection.State -eq 'Open') {
            $checkConnection.Close()
        }
        $checkConnection.Dispose()
    }
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
