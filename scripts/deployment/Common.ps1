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

function Test-IdempotentMigrationScriptRepaired {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $false
    }

    if ($Content -match '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*GO\s*UPDATE\s+Departments') {
        return $true
    }

    return $Content -match '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*END;\s*GO[\s\S]*?UPDATE\s+Departments'
}

function Repair-IdempotentMigrationScript {
    param([string]$Content)

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $Content
    }

    if (Test-IdempotentMigrationScriptRepaired -Content $Content) {
        return $Content
    }

    $idempotentPattern = '(?is)(ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*END;)(\s*)(IF NOT EXISTS\s*\(\s*\r?\n\s*SELECT \* FROM \[__EFMigrationsHistory\][\s\S]*?\r?\nBEGIN\s*\r?\n\s*UPDATE\s+Departments)'
    if ($Content -match $idempotentPattern) {
        return [regex]::Replace($Content, $idempotentPattern, '$1$2GO$2$3', 1)
    }

    $flatPattern = '(?is)(ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;)(\s*)(?=UPDATE\s+Departments)'
    return [regex]::Replace($Content, $flatPattern, '$1$2GO$2', 1)
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

    $databaseName = $null
    if ($builder.ContainsKey('Database')) {
        $databaseName = [string]$builder.Database
    }
    elseif ($builder.ContainsKey('Initial Catalog')) {
        $databaseName = [string]$builder['Initial Catalog']
    }
    else {
        $databaseName = [string]$builder.InitialCatalog
    }

    if ([string]::IsNullOrWhiteSpace($databaseName)) {
        throw "تعذر استخراج اسم قاعدة البيانات من إعداد الإنتاج."
    }

    return [pscustomobject]@{
        Server = $builder.DataSource
        Database = $databaseName
        ConnectionString = $builder.ConnectionString
    }
}

function Get-SqlRedactedConnectionLabel {
    param(
        [string]$Server,
        [string]$Database
    )

    return "Server=$Server; Database=$Database"
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

function Invoke-RobocopySafe {
    param(
        [string]$Source,
        [string]$Destination,
        [ValidateSet('Api', 'Web', 'Generic')]
        [string]$TargetType,
        [string[]]$ExtraArguments = @()
    )

    $arguments = @($Source, $Destination, '/E', '/R:2', '/W:2') + $ExtraArguments
    foreach ($argument in $arguments) {
        if ($TargetType -eq 'Api' -and $argument -match '(?i)^/MIR$') {
            throw "robocopy /MIR غير مسموح لملفات API."
        }
    }

    Ensure-Directory $Destination
    $null = & robocopy @arguments
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

    Invoke-RobocopySafe `
        -Source $ApiSource `
        -Destination $ApiTarget `
        -TargetType Api `
        -ExtraArguments @("/XF", "appsettings.json", "appsettings.Development.json", "appsettings.Production.json")

    if (Test-DirectoryHasContent $WebTarget) {
        Get-ChildItem -LiteralPath $WebTarget -Force | Remove-Item -Recurse -Force
    }

    Invoke-RobocopySafe -Source $WebSource -Destination $WebTarget -TargetType Web
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

function Get-LogLineTimestampUtc {
    param([string]$Line)

    if ($Line -match '(?i)^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)') {
        try {
            $parsed = [datetime]::Parse(
                $Matches[1],
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal)
            return $parsed.ToUniversalTime()
        }
        catch {
            return $null
        }
    }

    return $null
}

function Get-RecentLogTailLines {
    param(
        [string]$LogPath,
        [int]$TailLineCount
    )

    return ,@(Get-Content -LiteralPath $LogPath -Tail $TailLineCount -ErrorAction SilentlyContinue)
}

function Test-LogHasTimestampedLines {
    param([string[]]$Lines)

    foreach ($line in $Lines) {
        if (Get-LogLineTimestampUtc -Line $line) {
            return $true
        }
    }

    return $false
}

function Test-LogFileIsRecent {
    param(
        [string]$LogPath,
        [datetime]$SinceUtc,
        [bool]$UseFileWriteTimeFallback
    )

    if (-not $UseFileWriteTimeFallback) {
        return $true
    }

    $fileWriteUtc = (Get-Item -LiteralPath $LogPath).LastWriteTimeUtc
    return ($fileWriteUtc -ge $SinceUtc)
}

function Test-LogLineMatchesPattern {
    param(
        [string]$Line,
        [string[]]$Patterns
    )

    foreach ($pattern in $Patterns) {
        if ($Line -like "*$pattern*") {
            return $true
        }
    }

    return $false
}

function Add-RecentLogMatchedLine {
    param(
        [System.Collections.Generic.List[string]]$MatchedLines,
        [string]$Line
    )

    $MatchedLines.Add($Line.Trim()) | Out-Null
}

function Update-RecentLogState {
    param(
        [pscustomobject]$State,
        [string]$Line,
        [bool]$UseFileWriteTimeFallback
    )

    $timestamp = $null
    if (-not $UseFileWriteTimeFallback) {
        $timestamp = Get-LogLineTimestampUtc -Line $Line
        if ($timestamp) {
            $State.CurrentEventUtc = $timestamp
            $State.CaptureContinuation = $false
        }
    }

    return $timestamp
}

function Test-LogEventIsRecent {
    param(
        [pscustomobject]$State,
        [datetime]$SinceUtc,
        [bool]$UseFileWriteTimeFallback
    )

    if ($UseFileWriteTimeFallback) {
        return $true
    }

    return ($null -ne $State.CurrentEventUtc -and $State.CurrentEventUtc -ge $SinceUtc)
}

function Add-RecentLogLineIfApplicable {
    param(
        [string]$Line,
        $LineTimestamp,
        [pscustomobject]$State,
        [datetime]$SinceUtc,
        [string[]]$Patterns,
        [bool]$UseFileWriteTimeFallback,
        [System.Collections.Generic.List[string]]$MatchedLines
    )

    if (-not (Test-LogEventIsRecent -State $State -SinceUtc $SinceUtc -UseFileWriteTimeFallback $UseFileWriteTimeFallback)) {
        $State.CaptureContinuation = $false
        return
    }

    if (Test-LogLineMatchesPattern -Line $Line -Patterns $Patterns) {
        Add-RecentLogMatchedLine -MatchedLines $MatchedLines -Line $Line
        $State.CaptureContinuation = $true
        return
    }

    if ($State.CaptureContinuation -and -not $LineTimestamp) {
        if ([string]::IsNullOrWhiteSpace($Line)) {
            $State.CaptureContinuation = $false
        }
        else {
            Add-RecentLogMatchedLine -MatchedLines $MatchedLines -Line $Line
        }
    }
}

function Find-RecentLogErrors {
    param(
        [string[]]$Lines,
        [datetime]$SinceUtc,
        [string[]]$Patterns,
        [bool]$UseFileWriteTimeFallback
    )

    $matchedLines = New-Object System.Collections.Generic.List[string]
    $state = [pscustomobject]@{
        CurrentEventUtc = $null
        CaptureContinuation = $false
    }

    foreach ($line in $Lines) {
        $lineTimestamp = Update-RecentLogState `
            -State $state `
            -Line $line `
            -UseFileWriteTimeFallback $UseFileWriteTimeFallback

        Add-RecentLogLineIfApplicable `
            -Line $line `
            -LineTimestamp $lineTimestamp `
            -State $state `
            -SinceUtc $SinceUtc `
            -Patterns $Patterns `
            -UseFileWriteTimeFallback $UseFileWriteTimeFallback `
            -MatchedLines $matchedLines
    }

    return ,@($matchedLines | Select-Object -Unique)
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
        ),
        [int]$TailLineCount = 500
    )

    if (-not (Test-Path -LiteralPath $LogPath)) {
        return ,@()
    }

    $sinceUtc = $Since.ToUniversalTime()
    $lines = Get-RecentLogTailLines -LogPath $LogPath -TailLineCount $TailLineCount
    if (@($lines).Count -eq 0) {
        return ,@()
    }

    $useFileWriteTimeFallback = -not (Test-LogHasTimestampedLines -Lines $lines)
    if (-not (Test-LogFileIsRecent -LogPath $LogPath -SinceUtc $sinceUtc -UseFileWriteTimeFallback $useFileWriteTimeFallback)) {
        return ,@()
    }

    return Find-RecentLogErrors `
        -Lines $lines `
        -SinceUtc $sinceUtc `
        -Patterns $Patterns `
        -UseFileWriteTimeFallback $useFileWriteTimeFallback
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
        [string]$ConfigSource,
        [string]$BackupBrowsers = "",
        [string]$PlaywrightBrowsersPath = "",
        [string]$ReleaseManifestPath = "",
        [string]$BackupReleaseManifest = ""
    )

    if (-not (Test-DirectoryHasContent $BackupApi) -and -not (Test-DirectoryHasContent $BackupWeb) -and -not (Test-DirectoryHasContent $BackupBrowsers)) {
        Write-DeployInfo "لا توجد نسخة ملفات سابقة لاسترجاعها."
        return $false
    }

    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Stop-ApiListenersOnPort -Port $ApiPort | Out-Null
    Wait-PortReleased -Port $ApiPort -TimeoutSec 20 | Out-Null

    if (Test-DirectoryHasContent $BackupApi) {
        Invoke-RobocopySafe -Source $BackupApi -Destination $ApiTarget -TargetType Api
    }
    if (Test-DirectoryHasContent $BackupWeb) {
        if (Test-DirectoryHasContent $WebTarget) {
            Get-ChildItem -LiteralPath $WebTarget -Force | Remove-Item -Recurse -Force
        }
        Invoke-RobocopySafe -Source $BackupWeb -Destination $WebTarget -TargetType Web
    }
    if (Test-Path -LiteralPath $ConfigSource) {
        Copy-Item -LiteralPath $ConfigSource -Destination $ConfigTarget -Force
    }
    if ($PlaywrightBrowsersPath -and (Test-DirectoryHasContent $BackupBrowsers)) {
        Restore-PreviousDirectory -Target $PlaywrightBrowsersPath -PreviousPath $BackupBrowsers | Out-Null
    }
    if ($ReleaseManifestPath -and (Test-Path -LiteralPath $BackupReleaseManifest)) {
        Copy-Item -LiteralPath $BackupReleaseManifest -Destination $ReleaseManifestPath -Force
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
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,
        [string]$Database
    )

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw "سلسلة الاتصال مطلوبة."
    }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    if (-not [string]::IsNullOrWhiteSpace($Database)) {
        if ($builder.ContainsKey('Database')) {
            $builder.Database = $Database
        }
        else {
            $builder['Initial Catalog'] = $Database
        }
    }

    return New-Object System.Data.SqlClient.SqlConnection ($builder.ConnectionString)
}

function Invoke-SqlDeploymentCommand {
    param(
        [string]$Server,
        [string]$Database = 'master',
        [string]$ConnectionString,
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
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        throw "ConnectionString مطلوب لتنفيذ أوامر SQL."
    }

    $connection = New-SqlDeploymentConnection -ConnectionString $ConnectionString -Database $Database
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
        [string]$ConnectionString,
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
        [void](Invoke-SqlDeploymentCommand `
            -Server $Server `
            -Database 'master' `
            -ConnectionString $ConnectionString `
            -CommandText $verifySql)
    }
    catch {
        throw "فشل RESTORE VERIFYONLY للنسخة الاحتياطية. التفاصيل: $($_.Exception.Message)"
    }

    $headerSql = "RESTORE HEADERONLY FROM DISK = $diskLiteral;"
    $header = Invoke-SqlDeploymentCommand `
        -Server $Server `
        -Database 'master' `
        -ConnectionString $ConnectionString `
        -CommandText $headerSql `
        -DataTable
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
        [string]$ConnectionString,
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
        [void](Invoke-SqlDeploymentCommand `
            -Server $Server `
            -Database 'master' `
            -ConnectionString $ConnectionString `
            -CommandText $backupSql)
    }
    catch {
        throw "فشل BACKUP DATABASE. التفاصيل: $($_.Exception.Message)"
    }

    return Confirm-ProductionDatabaseBackupFile `
        -Server $Server `
        -Database $Database `
        -ConnectionString $ConnectionString `
        -BackupPath $backupPath
}

function Test-BackupPathIsProtected {
    param(
        [string]$CandidatePath,
        [string[]]$ProtectedPaths
    )

    foreach ($protectedPath in $ProtectedPaths) {
        if ([string]::Equals([string]$protectedPath, $CandidatePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
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

    return ,@($protected | ForEach-Object { [string]$_ })
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

    $protectedPaths = @(Get-ProtectedDatabaseBackupPaths -InstallRoot $InstallRoot)
    if ($LatestSuccessfulBackupPath) {
        $protectedPaths = @($protectedPaths + [string]$LatestSuccessfulBackupPath) | Select-Object -Unique
    }

    $deleted = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $allBackups.Count; $i++) {
        $candidate = $allBackups[$i]
        if ($i -lt $MinimumKeepCount) {
            continue
        }
        if (Test-BackupPathIsProtected -CandidatePath $candidate.FullName -ProtectedPaths $protectedPaths) {
            continue
        }

        Remove-Item -LiteralPath $candidate.FullName -Force
        $deleted.Add($candidate.FullName) | Out-Null
    }

    return ,@($deleted)
}

function Get-PlaywrightPackageVersionFromProject {
    param([string]$ProjectPath)

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "ملف المشروع غير موجود: $ProjectPath"
    }

    $content = Get-Content -LiteralPath $ProjectPath -Raw
    if ($content -match 'Include="Microsoft\.Playwright"\s+Version="([^"]+)"') {
        return $Matches[1]
    }

    throw "تعذر استخراج إصدار Microsoft.Playwright من: $ProjectPath"
}

function Test-SafeRelativePackagePath {
    param(
        [string]$RelativePath,
        [string]$Label = "المسار النسبي"
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "$Label فارغ."
    }

    $normalized = $RelativePath.Replace('/', '\').Trim()
    if ($normalized.StartsWith('\') -or $normalized.Contains(':')) {
        throw "$Label يحتوي مسارًا مطلقًا غير مسموح: $RelativePath"
    }

    if ($normalized -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Label يحتوي اجتياز مسار غير مسموح: $RelativePath"
    }

    return $normalized
}

function Resolve-SafePackagePath {
    param(
        [string]$PackageRoot,
        [string]$RelativePath
    )

    $safeRelative = Test-SafeRelativePackagePath -RelativePath $RelativePath
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $PackageRoot $safeRelative))
    $rootFull = [System.IO.Path]::GetFullPath($PackageRoot)
    if (-not $fullPath.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "المسار يخرج عن جذر الحزمة: $RelativePath"
    }

    return $fullPath
}

function Get-DirectorySizeBytes {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return (Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

function Assert-PlaywrightBrowserSourcePathSafe {
    param(
        [string]$SourcePath,
        [string]$StagingBrowsersPath,
        [string]$TempStagingRoot
    )

    $sourceFull = [System.IO.Path]::GetFullPath($SourcePath)
    $destinationFull = [System.IO.Path]::GetFullPath($StagingBrowsersPath)
    if ($sourceFull.Equals($destinationFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'مصدر Chromium يطابق مجلد الوجهة في الحزمة.'
    }

    $tempFull = [System.IO.Path]::GetFullPath($TempStagingRoot)
    $separator = [System.IO.Path]::DirectorySeparatorChar
    if (-not $tempFull.EndsWith([string]$separator)) {
        $tempFull += $separator
    }

    if ($sourceFull.StartsWith($tempFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'مصدر Chromium داخل مجلد staging المؤقت الذي سيُحذف.'
    }

    if (Test-Path -LiteralPath $sourceFull) {
        $sourceItem = Get-Item -LiteralPath $sourceFull -Force
        if (($sourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "مصدر Chromium غير موثوق (reparse point): $sourceFull"
        }
    }
}

function Get-RelativePathFromDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDirectory,

        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    $rootResolved = [System.IO.Path]::GetFullPath(
        (Resolve-Path -LiteralPath $RootDirectory).Path
    )

    $fullResolved = [System.IO.Path]::GetFullPath(
        (Resolve-Path -LiteralPath $FullPath).Path
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar

    $rootWithoutTrailingSeparator = $rootResolved.TrimEnd(
        [char[]]@($separator, $alternateSeparator)
    )

    if ($fullResolved.Equals(
        $rootWithoutTrailingSeparator,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        return ""
    }

    $rootPrefix = $rootWithoutTrailingSeparator + $separator

    if (-not $fullResolved.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase
    )) {
        throw "المسار يخرج عن الجذر المتوقع: $FullPath"
    }

    $relativePath = $fullResolved.Substring($rootPrefix.Length)

    if ($relativePath -match '(^|[/\\])\.\.([/\\]|$)') {
        throw "المسار يخرج عن الجذر المتوقع: $FullPath"
    }

    return ($relativePath -replace '\\', '/').TrimStart(
        [char[]]@('/', '\')
    )
}

function Get-PlaywrightBrowserExecutable {
    param([string]$BrowsersRoot)

    if (-not (Test-Path -LiteralPath $BrowsersRoot)) {
        throw "مجلد متصفحات Playwright غير موجود: $BrowsersRoot"
    }

    $candidates = Get-ChildItem -LiteralPath $BrowsersRoot -Recurse -File -Filter 'chrome.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '(?i)[/\\]chrome-win(?:64)?[/\\]chrome\.exe$' }

    if (-not $candidates -or @($candidates).Count -eq 0) {
        throw "لم يتم العثور على Chromium executable داخل: $BrowsersRoot"
    }

    if (@($candidates).Count -gt 1) {
        $relativeCandidates = $candidates | ForEach-Object {
            Get-RelativePathFromDirectory -RootDirectory $BrowsersRoot -FullPath $_.FullName
        }
        throw ("تم العثور على أكثر من Chromium executable غير قابل للحسم: " + ($relativeCandidates -join " | "))
    }

    $executable = $candidates[0]
    if ($executable.Length -le 0) {
        throw "ملف Chromium executable بحجم صفر: $($executable.FullName)"
    }

    $relativePath = Get-RelativePathFromDirectory -RootDirectory $BrowsersRoot -FullPath $executable.FullName
    return [pscustomobject]@{
        FullPath = $executable.FullName
        RelativePath = $relativePath
        SizeBytes = $executable.Length
    }
}

function Test-PlaywrightBrowserPayload {
    param(
        [string]$BrowsersRoot,
        [string]$ExpectedExecutableRelativePath = "",
        [string]$ExpectedExecutableSha256 = ""
    )

    $executable = Get-PlaywrightBrowserExecutable -BrowsersRoot $BrowsersRoot
    if ($ExpectedExecutableRelativePath) {
        $expected = ($ExpectedExecutableRelativePath -replace '\\', '/').TrimStart('/')
        $actual = $executable.RelativePath
        if ($expected -ne $actual) {
            throw "مسار Chromium غير مطابق. المتوقع: $expected الفعلي: $actual"
        }
    }

    if ($ExpectedExecutableSha256) {
        $actualHash = Get-FileSha256Hex -Path $executable.FullPath
        if ($actualHash -ne $ExpectedExecutableSha256.ToLowerInvariant()) {
            throw "تجزئة SHA256 لـ Chromium غير مطابقة."
        }
    }

    return $executable
}

function New-PlaywrightBrowserManifest {
    param(
        [string]$BrowsersRoot,
        [string]$PlaywrightScriptPath
    )

    $executable = Get-PlaywrightBrowserExecutable -BrowsersRoot $BrowsersRoot
    return [ordered]@{
        browser = 'chromium'
        installedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        executableRelativePath = $executable.RelativePath
        playwrightScriptSha256 = (Get-FileSha256Hex -Path $PlaywrightScriptPath)
        browserExecutableSha256 = (Get-FileSha256Hex -Path $executable.FullPath)
        browserDirectorySizeBytes = [long](Get-DirectorySizeBytes -Path $BrowsersRoot)
    }
}

function Assert-PlaywrightPackageHashes {
    param(
        [string]$PlaywrightScriptPath,
        [string]$ChromiumExecutablePath,
        [object]$PlaywrightManifest
    )

    $expectedScriptHash = [string]$PlaywrightManifest.playwrightScriptSha256
    if ($expectedScriptHash) {
        $actualScriptHash = Get-FileSha256Hex -Path $PlaywrightScriptPath
        if ($actualScriptHash -ne $expectedScriptHash.ToLowerInvariant()) {
            throw 'تجزئة SHA256 لملف playwright.ps1 غير مطابقة.'
        }
    }

    $expectedBrowserHash = [string]$PlaywrightManifest.browserExecutableSha256
    if ($expectedBrowserHash) {
        $actualBrowserHash = Get-FileSha256Hex -Path $ChromiumExecutablePath
        if ($actualBrowserHash -ne $expectedBrowserHash.ToLowerInvariant()) {
            throw 'تجزئة SHA256 لملف Chromium غير مطابقة.'
        }
    }
}

function Test-PlaywrightPackagePreflight {
    param(
        [string]$PackageRoot,
        [object]$Manifest
    )

    if (-not $Manifest.playwright) {
        throw 'manifest.json لا يحتوي قسم playwright.'
    }

    if (-not $Manifest.playwright.required) {
        throw 'حزمة الإنتاج تتطلب Chromium لكن playwright.required=false.'
    }

    $playwrightScript = Join-Path $PackageRoot 'api\playwright.ps1'
    if (-not (Test-Path -LiteralPath $playwrightScript)) {
        throw "ملف Playwright غير موجود في الحزمة: api\playwright.ps1"
    }

    $browserManifestPath = Join-Path $PackageRoot 'browsers\playwright-browser-manifest.json'
    if (-not (Test-Path -LiteralPath $browserManifestPath)) {
        throw 'ملف browsers\playwright-browser-manifest.json مفقود من الحزمة.'
    }

    $browserManifest = Get-Content -LiteralPath $browserManifestPath -Raw | ConvertFrom-Json
    $relativeExecutable = [string]$browserManifest.executableRelativePath
    $safeRelative = Test-SafeRelativePackagePath -RelativePath $relativeExecutable -Label 'executableRelativePath'
    $browsersRoot = Join-Path $PackageRoot 'browsers'
    $executablePath = Resolve-SafePackagePath -PackageRoot $browsersRoot -RelativePath $safeRelative

    if (-not (Test-Path -LiteralPath $executablePath)) {
        throw "ملف Chromium المشار إليه غير موجود: $relativeExecutable"
    }

    Assert-PlaywrightPackageHashes `
        -PlaywrightScriptPath $playwrightScript `
        -ChromiumExecutablePath $executablePath `
        -PlaywrightManifest $Manifest.playwright

    return [pscustomobject]@{
        BrowserManifest = $browserManifest
        ExecutablePath = $executablePath
        ExecutableRelativePath = $safeRelative
    }
}

function Copy-DirectoryAtomically {
    param(
        [string]$Source,
        [string]$Target
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "مصدر النسخ غير موجود: $Source"
    }

    $parent = Split-Path -Parent $Target
    Ensure-Directory $parent
    $stagingTarget = "$Target.next"

    if (Test-Path -LiteralPath $stagingTarget) {
        Remove-Item -LiteralPath $stagingTarget -Recurse -Force
    }

    Invoke-RobocopySafe -Source $Source -Destination $stagingTarget -TargetType Web
    Test-PlaywrightBrowserPayload -BrowsersRoot $stagingTarget | Out-Null

    $previousTarget = "$Target.previous"
    $previousPath = ""
    if (Test-Path -LiteralPath $Target) {
        if (Test-Path -LiteralPath $previousTarget) {
            Remove-Item -LiteralPath $previousTarget -Recurse -Force
        }
        Move-Item -LiteralPath $Target -Destination $previousTarget -Force
        $previousPath = $previousTarget
    }

    Move-Item -LiteralPath $stagingTarget -Destination $Target -Force
    return $previousPath
}

function Restore-PreviousDirectory {
    param(
        [string]$Target,
        [string]$PreviousPath
    )

    if (-not $PreviousPath -or -not (Test-Path -LiteralPath $PreviousPath)) {
        return $false
    }

    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
    }

    Move-Item -LiteralPath $PreviousPath -Destination $Target -Force
    return $true
}

function Remove-DirectoryIfExists {
    param([string]$Path)

    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-MigrationIdsFromDataTable {
    param([System.Data.DataTable]$Table)

    $migrationIds = New-Object System.Collections.Generic.List[string]
    foreach ($row in $Table.Rows) {
        $migrationId = ([string]$row.MigrationId).Trim()
        if ($migrationId) {
            [void]$migrationIds.Add($migrationId)
        }
    }

    return ,$migrationIds.ToArray()
}

function Resolve-MigrationIdsFromHandlerResult {
    param($Result)

    if ($null -eq $Result) {
        return ,@()
    }

    if ($Result -is [string]) {
        $migrationId = $Result.Trim()
        if ($migrationId) {
            return ,@($migrationId)
        }

        return ,@()
    }

    if ($Result -is [string[]]) {
        return ,@($Result | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    if ($Result -is [System.Data.DataTable]) {
        return Get-MigrationIdsFromDataTable -Table $Result
    }

    if ($Result -is [System.Data.DataRow]) {
        return Get-MigrationIdsFromDataTable -Table $Result.Table
    }

    if ($Result -is [System.Array]) {
        if ($Result.Count -gt 0 -and ($Result[0] -is [string])) {
            return ,@($Result | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
        }

        foreach ($item in $Result) {
            if ($item -is [System.Data.DataTable]) {
                return Get-MigrationIdsFromDataTable -Table $item
            }
        }
    }

    return ,@()
}

function Get-AppliedMigrationIds {
    param([string]$ConnectionString)

    if ($null -ne $global:SqlDeploymentCommandHandler) {
        $result = & $global:SqlDeploymentCommandHandler `
            -ConnectionString $ConnectionString `
            -CommandText 'SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];' `
            -DataTable:$true

        return Resolve-MigrationIdsFromHandlerResult -Result $result
    }

    $sql = 'SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory] ORDER BY [MigrationId];'
    $connection = New-SqlDeploymentConnection -ConnectionString $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $command.CommandTimeout = 0
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)
        $migrationIds = New-Object System.Collections.Generic.List[string]
        foreach ($row in $table.Rows) {
            $migrationId = ([string]$row.MigrationId).Trim()
            if ($migrationId) {
                [void]$migrationIds.Add($migrationId)
            }
        }
        return ,$migrationIds.ToArray()
    }
    finally {
        if ($connection.State -eq 'Open') {
            $connection.Close()
        }
        $connection.Dispose()
    }
}

function Test-RequiredMigrationApplied {
    param(
        [string]$ConnectionString,
        [string]$RequiredMigrationId
    )

    if ([string]::IsNullOrWhiteSpace($RequiredMigrationId)) {
        throw 'معرّف migration المطلوب فارغ.'
    }

    $appliedIds = Get-AppliedMigrationIds -ConnectionString $ConnectionString
    if ($null -eq $appliedIds) {
        $appliedList = @()
    }
    elseif ($appliedIds -is [string]) {
        $appliedList = @($appliedIds)
    }
    else {
        $appliedList = @($appliedIds | ForEach-Object { [string]$_ })
    }

    $normalizedRequired = $RequiredMigrationId.Trim()
    $found = $false
    foreach ($appliedId in $appliedList) {
        if ($appliedId.Trim() -ceq $normalizedRequired) {
            $found = $true
            break
        }
    }

    if (-not $found) {
        throw "لا يمكن تخطي migrations: قاعدة الإنتاج لا تحتوي migration المطلوبة $RequiredMigrationId."
    }
}

function Get-SafeArchiveDestinationRoot {
    param([string]$DestinationPath)

    $destinationFull = [System.IO.Path]::GetFullPath($DestinationPath)
    $separator = [System.IO.Path]::DirectorySeparatorChar
    if (-not $destinationFull.EndsWith([string]$separator)) {
        $destinationFull += $separator
    }

    Ensure-Directory $destinationFull
    return $destinationFull
}

function Test-ArchiveEntryNameIsUnsafe {
    param([string]$EntryName)

    if ([string]::IsNullOrWhiteSpace($EntryName)) {
        return $true
    }

    $normalized = $EntryName.Replace('/', '\')
    if ($normalized -match '(^|[\\/])\.\.([\\/]|$)') {
        return $true
    }

    if ($EntryName.StartsWith('/') -or $EntryName.StartsWith('\')) {
        return $true
    }

    if ($EntryName.Contains(':')) {
        return $true
    }

    if ($normalized.StartsWith('\\')) {
        return $true
    }

    if ($EntryName -match ':') {
        return $true
    }

    return $false
}

function Resolve-SafeArchiveTargetPath {
    param(
        [string]$DestinationRoot,
        [string]$EntryName
    )

    if (Test-ArchiveEntryNameIsUnsafe -EntryName $EntryName) {
        throw "تم رفض مسار غير آمن داخل ZIP: $EntryName"
    }

    $targetFull = [System.IO.Path]::GetFullPath((Join-Path $DestinationRoot $EntryName))
    if (-not $targetFull.StartsWith($DestinationRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "تم رفض مسار غير آمن داخل ZIP: $EntryName"
    }

    return $targetFull
}

function Test-ArchiveEntryIsReparsePoint {
    param([string]$TargetPath)

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return $false
    }

    $item = Get-Item -LiteralPath $TargetPath -Force
    return ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
}

function Expand-SafeArchiveEntry {
    param(
        [System.IO.Compression.ZipArchiveEntry]$Entry,
        [string]$DestinationRoot
    )

    $entryName = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($entryName)) {
        throw 'تم رفض إدخال ZIP باسم فارغ.'
    }

    $targetPath = Resolve-SafeArchiveTargetPath -DestinationRoot $DestinationRoot -EntryName $entryName
    if (Test-ArchiveEntryIsReparsePoint -TargetPath $targetPath) {
        throw "تم رفض reparse point داخل ZIP: $entryName"
    }

    if ($entryName.EndsWith('/') -or $entryName.EndsWith('\')) {
        Ensure-Directory $targetPath
        return
    }

    if ($Entry.Name -match ':') {
        throw "تم رفض alternate data stream داخل ZIP: $entryName"
    }

    $targetDirectory = Split-Path -Parent $targetPath
    Ensure-Directory $targetDirectory
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($Entry, $targetPath, $true)
}

function Expand-ArchiveSafely {
    param(
        [string]$ArchivePath,
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $destinationRoot = Get-SafeArchiveDestinationRoot -DestinationPath $DestinationPath

        foreach ($entry in $archive.Entries) {
            Expand-SafeArchiveEntry -Entry $entry -DestinationRoot $destinationRoot
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Write-ApiRunScript {
    param(
        [string]$RunScriptPath,
        [string]$ApiPath,
        [int]$ApiPort,
        [string]$ApiBindAddress = '0.0.0.0',
        [string]$PlaywrightBrowsersPath,
        [string]$LogPath
    )

    $apiExecutable = Join-Path $ApiPath 'Uqeb.Api.exe'
    $apiDll = Join-Path $ApiPath 'Uqeb.Api.dll'
    $launchCommand = if (Test-Path -LiteralPath $apiExecutable) {
        "`"$apiExecutable`""
    }
    else {
        "dotnet `"$apiDll`""
    }

    $content = @(
        '@echo off'
        "cd /d `"$ApiPath`""
        'set ASPNETCORE_ENVIRONMENT=Production'
        'set DOTNET_ENVIRONMENT=Production'
        "set ASPNETCORE_URLS=http://${ApiBindAddress}:$ApiPort"
        "set PLAYWRIGHT_BROWSERS_PATH=$PlaywrightBrowsersPath"
        "$launchCommand >> `"$LogPath`" 2>&1"
    ) -join "`r`n"

    Set-Content -LiteralPath $RunScriptPath -Value $content -Encoding ASCII
}

function Install-PlaywrightBrowserToProduction {
    param(
        [string]$PackageBrowsersSource,
        [string]$PlaywrightBrowsersPath
    )

    $parent = Split-Path -Parent $PlaywrightBrowsersPath
    Ensure-Directory $parent
    return Copy-DirectoryAtomically -Source $PackageBrowsersSource -Target $PlaywrightBrowsersPath
}

function Invoke-PlaywrightChromiumInstall {
    param(
        [string]$PlaywrightScriptPath,
        [string]$BrowsersRoot
    )

    if (-not (Test-Path -LiteralPath $PlaywrightScriptPath)) {
        throw "ملف Playwright غير موجود بعد نشر API: $PlaywrightScriptPath"
    }

    $previousBrowsersPath = $env:PLAYWRIGHT_BROWSERS_PATH
    $env:PLAYWRIGHT_BROWSERS_PATH = $BrowsersRoot
    try {
        Ensure-Directory $BrowsersRoot
        $null = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PlaywrightScriptPath install chromium 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "فشل تنزيل Chromium المتوافق مع Playwright برمز: $LASTEXITCODE"
        }
    }
    finally {
        if ($null -eq $previousBrowsersPath) {
            Remove-Item Env:PLAYWRIGHT_BROWSERS_PATH -ErrorAction SilentlyContinue
        }
        else {
            $env:PLAYWRIGHT_BROWSERS_PATH = $previousBrowsersPath
        }
    }

    return Get-PlaywrightBrowserExecutable -BrowsersRoot $BrowsersRoot
}