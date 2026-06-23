#Requires -Version 5.1
Set-StrictMode -Version Latest

function Write-DeployStep {
    param([string]$Message)
    Write-Output ""
    Write-Output ("==> " + $Message)
}

function Write-DeployInfo {
    param([string]$Message)
    Write-Output ("[معلومات] " + $Message)
}

function Write-DeployError {
    param([string]$Message)
    Write-Error $Message
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-RepositoryRoot {
    param([string]$Root)
    $apiProject = Join-Path $Root "backend\Uqeb.Api\Uqeb.Api.csproj"
    $frontend = Join-Path $Root "frontend\uqeb-ui\package.json"
    if (-not (Test-Path -LiteralPath $apiProject)) {
        throw "يجب تشغيل السكربت من جذر المشروع. المسار المتوقع: backend\Uqeb.Api\Uqeb.Api.csproj"
    }
    if (-not (Test-Path -LiteralPath $frontend)) {
        throw "يجب تشغيل السكربت من جذر المشروع. المسار المتوقع: frontend\uqeb-ui\package.json"
    }
}

function Test-CommandAvailable {
    param([string]$Name)
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    return $null -ne $command
}

function Get-FileSha256Hex {
    param([string]$Path)
    $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

function Get-TextSha256Hex {
    param([string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes)).Replace("-", "").ToLowerInvariant())
    }
    finally {
        $sha.Dispose()
    }
}

function Test-IsSqlGoSeparator {
    param(
        [string]$Line,
        [bool]$InSingleQuote,
        [bool]$InDoubleQuote
    )

    if ($InSingleQuote -or $InDoubleQuote) {
        return $false
    }

    $trimmed = $Line.Trim()
    return ($trimmed -match '^(?i)GO(?:\s+.*)?$')
}

function Update-SqlQuoteState {
    param(
        [string]$Line,
        [ref]$InSingleQuote,
        [ref]$InDoubleQuote
    )

    for ($i = 0; $i -lt $Line.Length; $i++) {
        $ch = $Line[$i]
        if ($ch -eq "'" -and -not $InDoubleQuote.Value) {
            if ($InSingleQuote.Value -and ($i + 1) -lt $Line.Length -and $Line[$i + 1] -eq "'") {
                $i++
                continue
            }
            $InSingleQuote.Value = -not $InSingleQuote.Value
        }
        elseif ($ch -eq '"' -and -not $InSingleQuote.Value) {
            $InDoubleQuote.Value = -not $InDoubleQuote.Value
        }
    }
}

function Add-SqlBatchIfNotEmpty {
    param(
        [System.Text.StringBuilder]$Builder,
        [System.Collections.Generic.List[string]]$Batches
    )

    $batchText = $Builder.ToString().Trim()
    if ($batchText.Length -gt 0) {
        $Batches.Add($batchText) | Out-Null
    }
    $Builder.Clear() | Out-Null
}

function Add-SqlLineToCurrentBatch {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Line
    )

    if ($Line.Length -gt 0) {
        [void]$Builder.AppendLine($Line)
    }
    else {
        [void]$Builder.AppendLine()
    }
}

function Split-SqlBatches {
    param([string]$SqlContent)

    $lines = $SqlContent -split "`r?`n"
    $batches = New-Object System.Collections.Generic.List[string]
    $current = New-Object System.Text.StringBuilder
    $inSingleQuote = $false
    $inDoubleQuote = $false

    foreach ($line in $lines) {
        if (Test-IsSqlGoSeparator -Line $line -InSingleQuote $inSingleQuote -InDoubleQuote $inDoubleQuote) {
            Add-SqlBatchIfNotEmpty -Builder $current -Batches $batches
            continue
        }

        Add-SqlLineToCurrentBatch -Builder $current -Line $line
        Update-SqlQuoteState -Line $line -InSingleQuote ([ref]$inSingleQuote) -InDoubleQuote ([ref]$inDoubleQuote)
    }

    Add-SqlBatchIfNotEmpty -Builder $current -Batches $batches

    return ,@($batches.ToArray())
}

function Repair-IdempotentMigrationScript {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $Content
    }

    if ($Content -match '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*GO\s*UPDATE\s+Departments') {
        return $Content
    }

    $pattern = '(?is)(ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;)(\s*)(?=UPDATE\s+Departments)'
    $repaired = [regex]::Replace($Content, $pattern, '$1$2GO$2', 1)
    return $repaired
}

function Get-SqlConnectionInfoFromSettings {
    param([string]$SettingsPath)

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        throw "ملف إعداد الإنتاج غير موجود: $SettingsPath"
    }

    $json = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $connectionString = [string]$json.ConnectionStrings.DefaultConnection
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        throw "سلسلة الاتصال DefaultConnection مفقودة في إعداد الإنتاج."
    }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionString
    if ([string]::IsNullOrWhiteSpace($builder.DataSource)) {
        throw "تعذر استخراج اسم خادم SQL من إعداد الإنتاج."
    }
    if ([string]::IsNullOrWhiteSpace($builder.InitialCatalog)) {
        throw "تعذر استخراج اسم قاعدة البيانات من إعداد الإنتاج."
    }

    return [pscustomobject]@{
        Server = $builder.DataSource
        Database = $builder.InitialCatalog
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Test-DirectoryHasContent {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }
    return $null -ne (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Invoke-RobocopyWithoutMirror {
    param(
        [string]$Source,
        [string]$Destination,
        [string[]]$ExtraArguments = @()
    )

    Ensure-Directory $Destination
    & robocopy $Source $Destination /E /R:2 /W:2 @ExtraArguments
    if ($LASTEXITCODE -ge 8) {
        throw "فشل robocopy برمز: $LASTEXITCODE"
    }
}

function Copy-ApplicationPayload {
    param(
        [string]$ApiSource,
        [string]$WebSource,
        [string]$ApiTarget,
        [string]$WebTarget
    )

    if ($ApiSource -match '/MIR' -or $WebSource -match '/MIR') {
        throw "النسخ باستخدام robocopy /MIR غير مسموح."
    }

    Invoke-RobocopyWithoutMirror `
        -Source $ApiSource `
        -Destination $ApiTarget `
        -ExtraArguments @("/XF", "appsettings.json", "appsettings.Development.json", "appsettings.Production.json")

    if (Test-DirectoryHasContent $WebTarget) {
        Get-ChildItem -LiteralPath $WebTarget -Force | Remove-Item -Recurse -Force
    }

    Invoke-RobocopyWithoutMirror -Source $WebSource -Destination $WebTarget
}

function Test-PortListener {
    param(
        [int]$Port,
        [int]$TimeoutSec = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($listener) {
            return $true
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Wait-PortReleased {
    param(
        [int]$Port,
        [int]$TimeoutSec = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if (-not $listener) {
            return $true
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Stop-ApiListenersOnPort {
    param([int]$Port)

    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    foreach ($listener in $listeners) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        if ($process -and $process.ProcessName -notin @("Uqeb.Api", "dotnet")) {
            throw "عملية غير متوقعة تستخدم المنفذ $Port : $($process.ProcessName)"
        }
        if ($process) {
            Stop-Process -Id $process.Id -Force
        }
    }
}

function Test-PackageManifestHashes {
    param(
        [string]$PackageRoot,
        [object]$Manifest
    )

    if (-not $Manifest.files) {
        throw "ملف manifest.json لا يحتوي على قائمة files."
    }

    foreach ($property in $Manifest.files.PSObject.Properties) {
        $relativePath = [string]$property.Name
        $expectedHash = [string]$property.Value
        $fullPath = Join-Path $PackageRoot ($relativePath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "ملف مفقود من الحزمة: $relativePath"
        }

        $actualHash = Get-FileSha256Hex -Path $fullPath
        if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
            throw "تجزئة SHA256 غير مطابقة للملف: $relativePath"
        }
    }
}

function Get-LatestMigrationId {
    param([string]$MigrationsDirectory)

    $files = Get-ChildItem -LiteralPath $MigrationsDirectory -Filter "*.cs" |
        Where-Object { $_.Name -notmatch '\.Designer\.cs$' -and $_.Name -ne 'AppDbContextModelSnapshot.cs' } |
        Sort-Object Name

    if (-not $files) {
        throw "لم يتم العثور على ملفات migrations."
    }

    $last = $files[-1].BaseName
    return $last
}

function Test-RecentLogErrors {
    param(
        [string]$LogPath,
        [datetime]$Since,
        [string[]]$Patterns = @(
            'SqlException',
            'Invalid column',
            'Invalid object',
            'Unhandled exception'
        )
    )

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return ,@()
    }

    $matchedLines = New-Object System.Collections.Generic.List[string]
    $lines = Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue
    foreach ($line in $lines) {
        foreach ($pattern in $Patterns) {
            if ($line -like "*$pattern*") {
                $matchedLines.Add($line.Trim()) | Out-Null
                break
            }
        }
    }

    return ,@($matchedLines | Select-Object -Unique)
}

function Invoke-DeploymentFileRollback {
    param(
        [string]$TaskName,
        [int]$ApiPort,
        [string]$BackupApi,
        [string]$BackupWeb,
        [string]$ApiTarget,
        [string]$WebTarget,
        [string]$ConfigTarget,
        [string]$ConfigSource
    )

    if (-not (Test-DirectoryHasContent $BackupApi) -and -not (Test-DirectoryHasContent $BackupWeb)) {
        Write-DeployInfo "لا توجد نسخة ملفات سابقة لاسترجاعها."
        return $false
    }

    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Stop-ApiListenersOnPort -Port $ApiPort | Out-Null
    Wait-PortReleased -Port $ApiPort -TimeoutSec 20 | Out-Null

    if (Test-DirectoryHasContent $BackupApi) {
        Invoke-RobocopyWithoutMirror -Source $BackupApi -Destination $ApiTarget
    }
    if (Test-DirectoryHasContent $BackupWeb) {
        if (Test-DirectoryHasContent $WebTarget) {
            Get-ChildItem -LiteralPath $WebTarget -Force | Remove-Item -Recurse -Force
        }
        Invoke-RobocopyWithoutMirror -Source $BackupWeb -Destination $WebTarget
    }
    if (Test-Path -LiteralPath $ConfigSource) {
        Copy-Item -LiteralPath $ConfigSource -Destination $ConfigTarget -Force
    }

    Start-ScheduledTask -TaskName $TaskName
    return $true
}

function Read-Sha256SidecarFile {
    param([string]$Sha256FilePath)

    if (-not (Test-Path -LiteralPath $Sha256FilePath)) {
        throw "ملف SHA256 المرافق غير موجود: $Sha256FilePath"
    }

    $content = (Get-Content -LiteralPath $Sha256FilePath -Raw).Trim()
    if ($content -match '(?i)^([a-f0-9]{64})\b') {
        return $Matches[1].ToLowerInvariant()
    }

    throw "تنسيق ملف SHA256 غير صالح: $Sha256FilePath"
}

function Find-Sha256SidecarPath {
    param([string]$ZipPath)

    $directory = Split-Path -Parent $ZipPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ZipPath)
    $candidate = Join-Path $directory ($baseName + ".sha256.txt")
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    throw "لم يتم العثور على ملف SHA256 بجانب الحزمة: $candidate"
}

function Assert-DatabaseBackupNotBypassed {
  if (-not [string]::IsNullOrWhiteSpace($env:UQEB_SKIP_DATABASE_BACKUP)) {
        throw "تجاوز النسخة الاحتياطية لقاعدة البيانات غير مسموح عبر متغير البيئة UQEB_SKIP_DATABASE_BACKUP."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:SKIP_DATABASE_BACKUP)) {
        throw "تجاوز النسخة الاحتياطية لقاعدة البيانات غير مسموح عبر متغير البيئة SKIP_DATABASE_BACKUP."
    }
}

function Get-SqlBracketedIdentifier {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "اسم SQL فارغ."
    }
    if ($Name -match '[^\w\.]') {
        throw "اسم SQL غير صالح: $Name"
    }
    $parts = $Name -split '\.'
    return (($parts | ForEach-Object { "[$_]" }) -join '.')
}

function Get-SqlLiteralPath {
    param([string]$Path)
    return ("N'" + ($Path -replace "'", "''") + "'")
}

function New-SqlDeploymentConnection {
    param(
        [string]$Server,
        [string]$Database = 'master'
    )

    $connectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
    return New-Object System.Data.SqlClient.SqlConnection $connectionString
}

function Invoke-SqlDeploymentCommand {
    param(
        [string]$Server,
        [string]$Database = 'master',
        [string]$CommandText,
        [switch]$Scalar,
        [switch]$DataTable
    )

    if ($null -ne $global:SqlDeploymentCommandHandler) {
        return ,(& $global:SqlDeploymentCommandHandler `
            -Server $Server `
            -Database $Database `
            -CommandText $CommandText `
            -Scalar:([bool]$Scalar) `
            -DataTable:([bool]$DataTable))
    }

    if ([string]::IsNullOrWhiteSpace($CommandText)) {
        throw "أمر SQL فارغ."
    }

    $connection = New-SqlDeploymentConnection -Server $Server -Database $Database
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $CommandText
        $command.CommandTimeout = 0

        if ($Scalar) {
            return $command.ExecuteScalar()
        }
        if ($DataTable) {
            $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
            $table = New-Object System.Data.DataTable
            [void]$adapter.Fill($table)
            return ,$table
        }

        [void]$command.ExecuteNonQuery()
        return $null
    }
    finally {
        if ($connection.State -eq 'Open') {
            $connection.Close()
        }
        $connection.Dispose()
    }
}

function Get-ProductionDatabaseBackupPath {
    param(
        [string]$BackupDirectory,
        [string]$Database,
        [string]$Timestamp
    )

    $fileName = "{0}-before-{1}.bak" -f $Database, $Timestamp
    return Join-Path $BackupDirectory $fileName
}

function Get-ManualDatabaseRestoreCommand {
    param(
        [string]$Server,
        [string]$Database,
        [string]$BackupPath
    )

    $databaseId = Get-SqlBracketedIdentifier -Name $Database
    $diskLiteral = Get-SqlLiteralPath -Path $BackupPath
    return @(
        "-- استعادة يدوية لقاعدة البيانات (نفّذ بحذر على خادم: $Server)"
        "ALTER DATABASE $databaseId SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
        "RESTORE DATABASE $databaseId FROM DISK = $diskLiteral WITH REPLACE, CHECKSUM;"
        "ALTER DATABASE $databaseId SET MULTI_USER;"
    ) -join [Environment]::NewLine
}

function Confirm-ProductionDatabaseBackupFile {
    param(
        [string]$Server,
        [string]$Database,
        [string]$BackupPath
    )

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        throw "ملف النسخة الاحتياطية غير موجود: $BackupPath"
    }

    $fileInfo = Get-Item -LiteralPath $BackupPath
    if ($fileInfo.Length -le 0) {
        throw "ملف النسخة الاحتياطية بحجم صفر: $BackupPath"
    }

    $diskLiteral = Get-SqlLiteralPath -Path $BackupPath
    $verifySql = "RESTORE VERIFYONLY FROM DISK = $diskLiteral WITH CHECKSUM;"
    try {
        [void](Invoke-SqlDeploymentCommand -Server $Server -Database 'master' -CommandText $verifySql)
    }
    catch {
        throw "فشل RESTORE VERIFYONLY للنسخة الاحتياطية. التفاصيل: $($_.Exception.Message)"
    }

    $headerSql = "RESTORE HEADERONLY FROM DISK = $diskLiteral;"
    $header = Invoke-SqlDeploymentCommand -Server $Server -Database 'master' -CommandText $headerSql -DataTable
    if ($header -is [System.Data.DataRow]) {
        $header = $header.Table
    }
    if (-not ($header -is [System.Data.DataTable]) -or $header.Rows.Count -eq 0) {
        throw "تعذر قراءة RESTORE HEADERONLY للنسخة الاحتياطية."
    }

    $backupDatabaseName = [string]$header.Rows[0]['DatabaseName']
    if ([string]::IsNullOrWhiteSpace($backupDatabaseName)) {
        throw "تعذر تحديد اسم قاعدة البيانات من النسخة الاحتياطية."
    }
    if ($backupDatabaseName -ne $Database) {
        throw "النسخة الاحتياطية تخص قاعدة '$backupDatabaseName' وليست '$Database'."
    }

    try {
        $sha256 = Get-FileSha256Hex -Path $BackupPath
    }
    catch {
        throw "تعذر حساب تجزئة SHA256 للنسخة الاحتياطية: $($_.Exception.Message)"
    }
    if ([string]::IsNullOrWhiteSpace($sha256)) {
        throw "تجزئة SHA256 للنسخة الاحتياطية فارغة."
    }

    return [pscustomobject]@{
        Path = $BackupPath
        SizeBytes = $fileInfo.Length
        CreatedAtUtc = $fileInfo.LastWriteTimeUtc.ToString('o')
        Sha256 = $sha256
        DatabaseName = $backupDatabaseName
    }
}

function Invoke-ProductionDatabaseBackup {
    param(
        [string]$Server,
        [string]$Database,
        [string]$BackupDirectory,
        [string]$Timestamp
    )

    Assert-DatabaseBackupNotBypassed
    Ensure-Directory $BackupDirectory

    $backupPath = Get-ProductionDatabaseBackupPath `
        -BackupDirectory $BackupDirectory `
        -Database $Database `
        -Timestamp $Timestamp

    $databaseId = Get-SqlBracketedIdentifier -Name $Database
    $diskLiteral = Get-SqlLiteralPath -Path $backupPath
    $backupSql = "BACKUP DATABASE $databaseId TO DISK = $diskLiteral WITH CHECKSUM, INIT, STATS = 5;"

    if ($backupSql -notmatch 'WITH CHECKSUM') {
        throw "أمر BACKUP DATABASE لا يستخدم WITH CHECKSUM."
    }

    try {
        [void](Invoke-SqlDeploymentCommand -Server $Server -Database 'master' -CommandText $backupSql)
    }
    catch {
        throw "فشل BACKUP DATABASE. التفاصيل: $($_.Exception.Message)"
    }

    return Confirm-ProductionDatabaseBackupFile `
        -Server $Server `
        -Database $Database `
        -BackupPath $backupPath
}

function Get-ProtectedDatabaseBackupPaths {
    param([string]$InstallRoot)

    $protected = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $candidates = @(
        Join-Path $InstallRoot 'publish\release-manifest.json'
    )

    $backupRoot = Join-Path $InstallRoot 'backup'
    if (Test-Path -LiteralPath $backupRoot) {
        $candidates += Get-ChildItem -LiteralPath $backupRoot -Directory -Filter 'before-*' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'release-manifest.json' }
    }

    foreach ($manifestPath in $candidates) {
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            continue
        }
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($manifest.databaseBackup -and $manifest.databaseBackup.path) {
                [void]$protected.Add([string]$manifest.databaseBackup.path)
            }
        }
        catch {
            continue
        }
    }

    return ,@($protected)
}

function Invoke-DatabaseBackupRetentionPolicy {
    param(
        [string]$BackupDirectory,
        [string]$InstallRoot,
        [int]$MinimumKeepCount = 10,
        [string]$LatestSuccessfulBackupPath
    )

    if (-not (Test-Path -LiteralPath $BackupDirectory)) {
        return @()
    }

    $allBackups = Get-ChildItem -LiteralPath $BackupDirectory -Filter '*.bak' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending

    if (-not $allBackups) {
        return @()
    }

    $protectedPaths = Get-ProtectedDatabaseBackupPaths -InstallRoot $InstallRoot
    if ($LatestSuccessfulBackupPath) {
        $protectedPaths = @($protectedPaths + $LatestSuccessfulBackupPath) | Select-Object -Unique
    }

    $deleted = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $allBackups.Count; $i++) {
        $candidate = $allBackups[$i]
        if ($i -lt $MinimumKeepCount) {
            continue
        }
        if ($protectedPaths -contains $candidate.FullName) {
            continue
        }

        Remove-Item -LiteralPath $candidate.FullName -Force
        $deleted.Add($candidate.FullName) | Out-Null
    }

    return ,@($deleted)
}
