#Requires -Version 5.1

BeforeAll {
    function Resolve-DeploymentScriptsRoot {
        $candidate = $PSScriptRoot
        $commonPath = Join-Path $candidate 'deployment\Common.ps1'
        if (-not (Test-Path -LiteralPath $commonPath -PathType Leaf)) {
            throw "Helper script missing: Common.ps1 could not be found at $commonPath"
        }

        return $candidate
    }

    $script:RepoScriptsRoot = Resolve-DeploymentScriptsRoot

    $script:DeploymentHelpersPath = Join-Path `
        $script:RepoScriptsRoot `
        'tests\DeploymentTestHelpers.ps1'

    if (-not (Test-Path -LiteralPath $script:DeploymentHelpersPath -PathType Leaf)) {
        throw "Helper script missing: DeploymentTestHelpers.ps1 could not be found at $script:DeploymentHelpersPath"
    }

    . $script:DeploymentHelpersPath

    if (-not (Get-Command -Name 'Initialize-DeploymentTestSession' -ErrorAction SilentlyContinue)) {
        throw "Required function 'Initialize-DeploymentTestSession' is not defined after dot-sourcing DeploymentTestHelpers.ps1."
    }

    $script:CommonPath = Join-Path $script:RepoScriptsRoot 'deployment\Common.ps1'
    $script:InstallScript = Join-Path $script:RepoScriptsRoot 'install-production-package.ps1'
    . $script:CommonPath
    if (-not (Get-Command -Name 'Invoke-SqlDeploymentCommand' -ErrorAction SilentlyContinue)) {
        throw "Required function 'Invoke-SqlDeploymentCommand' is not defined after dot-sourcing Common.ps1."
    }
    $script:DeploymentTest = Initialize-DeploymentTestSession -ScriptsRoot $script:RepoScriptsRoot
    $script:ApplyScript = Join-Path $script:RepoScriptsRoot 'apply-migrations.ps1'

    function New-TempDirectory {
        param([string]$Prefix = 'uqeb-deploy-test-')
        return New-DeploymentTestTempDirectory -Prefix $Prefix
    }

    function New-TestRestoreHeaderTable {
        param([string]$DatabaseName = 'UqebDb')
        $table = New-Object System.Data.DataTable
        [void]$table.Columns.Add('DatabaseName', [string])
        $row = $table.NewRow()
        $row['DatabaseName'] = $DatabaseName
        [void]$table.Rows.Add($row)
        return $table
    }

    function New-TestSqlHeaderHandlerBody {
        $table = New-Object System.Data.DataTable
        [void]$table.Columns.Add('DatabaseName', [string])
        $row = $table.NewRow()
        $row['DatabaseName'] = 'UqebDb'
        [void]$table.Rows.Add($row)
        return $table
    }

    $global:NewTestSqlHeaderHandlerBody = ${function:New-TestSqlHeaderHandlerBody}

    function New-TestDatabasePassword {
        return "Tst-$([guid]::NewGuid().ToString('N'))!"
    }

    function New-TestSqlConnectionString {
        param(
            [ValidateSet('Integrated', 'SqlLogin')]
            [string]$AuthenticationMode,
            [string]$Password
        )

        if ($AuthenticationMode -eq 'Integrated') {
            return 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
        }

        if ([string]::IsNullOrWhiteSpace($Password)) {
            $Password = New-TestDatabasePassword
        }

        return "Server=.;Database=UqebDb;User ID=uqeb;Password=$Password;TrustServerCertificate=True"
    }

    function New-TestProductionSettings {
        param([string]$ConnectionString)

        $settingsPath = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        @{
            ConnectionStrings = @{
                DefaultConnection = $ConnectionString
            }
        } | ConvertTo-Json -Compress | Set-Content -LiteralPath $settingsPath -Encoding UTF8

        return $settingsPath
    }

    function Get-TestSqlConnectionBuilder {
        param([string]$ConnectionString)

        return New-Object System.Data.SqlClient.SqlConnectionStringBuilder $ConnectionString
    }

    function Write-TestReleaseManifestWithBackup {
        param(
            [string]$InstallRoot,
            [string]$BackupPath
        )

        $manifest = [ordered]@{
            databaseBackup = [ordered]@{ path = $BackupPath }
        }
        Ensure-Directory (Join-Path $InstallRoot 'publish')
        $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $InstallRoot 'publish\release-manifest.json') -Encoding UTF8
    }

    function New-TestBackupFileSet {
        param(
            [string]$BackupDir,
            [int]$Count = 12
        )

        $files = @()
        for ($i = 0; $i -lt $Count; $i++) {
            $path = Join-Path $BackupDir ("UqebDb-before-20260101-12000$i.bak")
            Set-Content -LiteralPath $path -Value ('x' * 10) -Encoding ASCII
            (Get-Item -LiteralPath $path).LastWriteTime = (Get-Date).AddHours(-$i)
            $files += $path
        }

        return ,@($files)
    }

    if (-not (Get-Command robocopy -ErrorAction SilentlyContinue)) {
        function script:robocopy {
            param($args)
            $global:LASTEXITCODE = 1
        }
    }
}

Describe 'PowerShell deployment script parse checks' {
    It 'parses install-production-package.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $script:InstallScript,
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses build-production-package.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $PSScriptRoot 'build-production-package.ps1'),
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses deploy-production-v2.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $PSScriptRoot 'deploy-production-v2.ps1'),
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'runs PowerShell encoding policy in deployment package CI' {
        $repoRoot = Split-Path $PSScriptRoot -Parent
        $workflowPath = Join-Path (Join-Path (Join-Path $repoRoot '.github') 'workflows') 'deployment-package.yml'
        $workflow = Get-Content -LiteralPath $workflowPath -Raw
        $workflow | Should -Match 'scripts[\\/]tests[\\/]PowerShellEncoding\.Tests\.ps1'
    }
}

Describe 'Production deployment wrapper safety' {
    It 'rejects ZIP packages instead of dropping API bind settings' {
        $scriptPath = Join-Path $PSScriptRoot 'deploy-production-v2.ps1'

        {
            & $scriptPath `
                -SourcePackagePath 'C:\Uqeb\incoming\Uqeb-test.zip' `
                -ApiBindAddress '10.0.177.17' `
                -ApiPort 5000
        } | Should -Throw '*install-production-package.ps1*ApiBindAddress*10.0.177.17*'
    }

    It 'writes legacy run-api wrapper with all-interface binding' {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'deploy-production-v2.ps1') -Raw
        $content | Should -Match '\$effectiveApiBinding = "http://0\.0\.0\.0:\$ApiPort"'
        $content | Should -Match 'ASPNETCORE_URLS=\$effectiveApiBinding'
        $content | Should -Not -Match 'ASPNETCORE_URLS=http://\$ApiBindAddress'
        $content | Should -Match 'ApiBinding=\$effectiveApiBinding'
        $content | Should -Not -Match 'ApiBinding=http://\$ApiBindAddress'
    }
}

Describe 'Production build toolchain version gates' {
    It 'accepts Node and npm versions inside package engines range' {
        {
            Assert-ToolVersionRangeExpression `
                -ToolName 'Node.js' `
                -ActualVersion 'v24.12.0' `
                -RangeExpression '>=24 <25'
        } | Should -Not -Throw

        {
            Assert-ToolVersionRangeExpression `
                -ToolName 'npm' `
                -ActualVersion '11.7.0' `
                -RangeExpression '>=11 <12'
        } | Should -Not -Throw
    }

    It 'rejects Node and npm versions outside package engines range' {
        {
            Assert-ToolVersionRangeExpression `
                -ToolName 'Node.js' `
                -ActualVersion '23.9.0' `
                -RangeExpression '>=24 <25'
        } | Should -Throw '*Node.js version*'

        {
            Assert-ToolVersionRangeExpression `
                -ToolName 'npm' `
                -ActualVersion '12.0.0' `
                -RangeExpression '>=11 <12'
        } | Should -Throw '*npm version*'
    }

    It 'enforces the global.json .NET SDK feature band' {
        {
            Assert-DotNetSdkVersionMatchesGlobalJson `
                -ActualVersion '10.0.305' `
                -RequiredVersion '10.0.301'
        } | Should -Not -Throw

        {
            Assert-DotNetSdkVersionMatchesGlobalJson `
                -ActualVersion '10.0.401' `
                -RequiredVersion '10.0.301'
        } | Should -Throw '*.NET SDK version*'
    }
}

Describe 'SHA256 package validation' {
    It 'rejects ZIP when SHA256 sidecar is missing' {
        $zip = New-TempDirectory
        $zipFile = Join-Path $zip 'Uqeb-test.zip'
        Set-Content $zipFile 'zip' -Encoding ASCII

        { Find-Sha256SidecarPath -ZipPath $zipFile } | Should -Throw '*SHA256*'
    }

    It 'rejects mismatched SHA256 hash' {
        $root = New-TempDirectory
        $zipFile = Join-Path $root 'pkg.zip'
        Set-Content $zipFile 'zip-content' -Encoding ASCII
        $shaFile = Join-Path $root 'pkg.sha256.txt'
        Set-Content $shaFile '0000000000000000000000000000000000000000000000000000000000000000  pkg.zip' -Encoding ASCII

        $expected = Read-Sha256SidecarFile -Sha256FilePath $shaFile
        $actual = Get-FileSha256Hex -Path $zipFile
        ($expected -eq $actual) | Should -BeFalse
    }
}

Describe 'Package content validation' {
    It 'rejects incomplete package missing manifest entries' {
        $root = New-TempDirectory
        New-TestPackage -Root $root -CommonPath $script:CommonPath
        Remove-Item (Join-Path $root 'api\Uqeb.Api.dll') -Force
        $manifest = Get-Content (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json

        { Test-PackageManifestHashes -PackageRoot $root -Manifest $manifest } |
            Should -Throw
    }
}

Describe 'SQL batch splitting' {
    It 'splits only on standalone GO lines' {
        $sql = @"
SELECT 1;
GO
SELECT 2;
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 2
        $batches[0] | Should -Match 'SELECT 1'
        $batches[1] | Should -Match 'SELECT 2'
    }

    It 'does not split on GO inside a single-quoted string' {
        $sql = @"
SELECT 'GO value';
SELECT 2;
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match "GO value"
    }

    It 'does not split on GO inside a double-quoted string' {
        $sql = @"
SELECT "GO value";
SELECT 2;
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match 'GO value'
    }

    It 'handles escaped single quotes inside strings' {
        $sql = "SELECT 'it''s GO';"
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match "it''s GO"
    }

    It 'does not split on GO inside a multiline single-quoted string' {
        $sql = @"
SELECT 'line1
GO
line2';
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match 'line1'
        $batches[0] | Should -Match 'line2'
    }

    It 'ignores empty batches from repeated GO separators' {
        $sql = @"
GO
GO
SELECT 1;
GO
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match 'SELECT 1'
    }

    It 'treats GO separators case-insensitively' {
        $sql = @"
SELECT 1;
go
SELECT 2;
Go
SELECT 3;
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 3
        $batches[0] | Should -Match 'SELECT 1'
        $batches[1] | Should -Match 'SELECT 2'
        $batches[2] | Should -Match 'SELECT 3'
    }

    It 'keeps the final batch when SQL ends without GO' {
        $sql = @"
SELECT 1;
SELECT 2;
"@
        $batches = Split-SqlBatches -SqlContent $sql
        $batches.Count | Should -Be 1
        $batches[0] | Should -Match 'SELECT 1'
        $batches[0] | Should -Match 'SELECT 2'
    }

    It 'repairs idempotent migration script with GO before NameNormalized usage' {
        $sql = @"
ALTER TABLE [ExternalParties] ADD [NameNormalized] nvarchar(450) NOT NULL DEFAULT N'';
ALTER TABLE [Departments] ADD [NameNormalized] nvarchar(450) NOT NULL DEFAULT N'';
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(450) NOT NULL DEFAULT N'';
UPDATE Departments SET NameNormalized = LOWER(Name);
"@
        $fixed = Repair-IdempotentMigrationScript -Content $sql
        Test-IdempotentMigrationScriptRepaired -Content $fixed | Should -BeTrue
    }

    It 'repairs EF idempotent migration blocks with GO before NameNormalized usage' {
        $sql = @"
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622062754_AddReferenceDataNormalizedNames'
)
BEGIN
    ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(450) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260622062754_AddReferenceDataNormalizedNames'
)
BEGIN
    UPDATE Departments
    SET NameNormalized = LOWER(Name);
END;
"@
        $fixed = Repair-IdempotentMigrationScript -Content $sql
        Test-IdempotentMigrationScriptRepaired -Content $fixed | Should -BeTrue
    }
}

Describe 'Recent log error scanning' {
    It 'does not return errors before Since' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $oldTs = (Get-Date).ToUniversalTime().AddHours(-2).ToString('o')
        $recentTs = (Get-Date).ToUniversalTime().AddMinutes(-5).ToString('o')
        @(
            "$oldTs fail: SqlException: old error"
            "$recentTs fail: SqlException: recent error"
        ) | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 1
        $result[0] | Should -Match 'recent error'
        $result[0] | Should -Not -Match 'old error'
    }

    It 'returns matching log lines after Since' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $recentTs = (Get-Date).ToUniversalTime().AddMinutes(-5).ToString('o')
        @(
            "$recentTs info line"
            "$recentTs fail: SqlException: timeout"
            "$recentTs fail: SqlException: deadlock"
        ) | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        $result.Count | Should -Be 2
        ($result -join ' ') | Should -Match 'timeout'
        ($result -join ' ') | Should -Match 'deadlock'
    }

    It 'includes stack trace lines for a recent timestamped event' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $recentTs = (Get-Date).ToUniversalTime().AddMinutes(-1).ToString('o')
        @(
            "$recentTs fail: Unhandled exception: boom"
            '   at Contoso.Service.Worker()'
        ) | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 2
        ($result -join ' ') | Should -Match 'Unhandled exception'
        ($result -join ' ') | Should -Match 'at Contoso'
    }

    It 'does not treat historical errors as deployment failures' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $oldTs = (Get-Date).ToUniversalTime().AddDays(-2).ToString('o')
        "$oldTs fail: SqlException: ancient" | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 0
    }

    It 'removes duplicate matching lines' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $recentTs = (Get-Date).ToUniversalTime().AddMinutes(-1).ToString('o')
        @(
            "$recentTs fail: SqlException: same"
            "$recentTs fail: SqlException: same"
        ) | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 1
        $result[0] | Should -Match 'SqlException: same'
    }

    It 'returns an empty array when the log file is missing' {
        $missing = Join-Path (New-TempDirectory) 'missing.log'
        $result = Test-RecentLogErrors -LogPath $missing -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 0
    }

    It 'does not use the automatic $Matches variable' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $recentTs = (Get-Date).ToUniversalTime().AddMinutes(-1).ToString('o')
        "$recentTs fail: Unhandled exception in pipeline" | Set-Content -LiteralPath $log -Encoding UTF8
        $script:Matches = @('stale')

        try {
            $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
            @($result).Count | Should -Be 1
            $result[0] | Should -Match 'Unhandled exception'
            $script:Matches | Should -Be @('stale')
        }
        finally {
            Remove-Variable -Name Matches -Scope Script -ErrorAction SilentlyContinue
        }
    }

    It 'parses ISO timestamps in log lines' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        $iso = '2026-06-11T10:15:30.1234567Z fail: SqlException: iso'
        $iso | Set-Content -LiteralPath $log -Encoding UTF8

        $result = Test-RecentLogErrors -LogPath $log -Since ([datetime]'2026-06-11T10:15:00Z')
        @($result).Count | Should -Be 1
        $result[0] | Should -Match 'iso'
    }

    It 'uses LastWriteTimeUtc fallback when log has no timestamps' {
        $log = Join-Path (New-TempDirectory) 'api.log'
        'fail: SqlException: no timestamp' | Set-Content -LiteralPath $log -Encoding UTF8
        (Get-Item -LiteralPath $log).LastWriteTimeUtc = (Get-Date).ToUniversalTime().AddDays(-2)

        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 0

        (Get-Item -LiteralPath $log).LastWriteTimeUtc = (Get-Date).ToUniversalTime()
        $result = Test-RecentLogErrors -LogPath $log -Since (Get-Date).ToUniversalTime().AddHours(-1)
        @($result).Count | Should -Be 1
    }
}

Describe 'Recent log scan helpers' {
    It 'Update-RecentLogState resets continuation on a new timestamp' {
        $state = [pscustomobject]@{
            CurrentEventUtc = $null
            CaptureContinuation = $true
        }
        $line = '2026-06-11T10:00:00Z info'
        $timestamp = Update-RecentLogState -State $state -Line $line -UseFileWriteTimeFallback $false
        $timestamp | Should -Not -BeNullOrEmpty
        $state.CaptureContinuation | Should -BeFalse
    }

    It 'Test-LogEventIsRecent returns false before SinceUtc' {
        $state = [pscustomobject]@{
            CurrentEventUtc = [datetime]'2026-06-11T08:00:00Z'
            CaptureContinuation = $false
        }
        Test-LogEventIsRecent `
            -State $state `
            -SinceUtc ([datetime]'2026-06-11T09:00:00Z') `
            -UseFileWriteTimeFallback $false | Should -BeFalse
    }

    It 'Add-RecentLogLineIfApplicable captures continuation lines' {
        $state = [pscustomobject]@{
            CurrentEventUtc = [datetime]'2026-06-11T10:00:00Z'
            CaptureContinuation = $false
        }
        $matchedLines = New-Object System.Collections.Generic.List[string]
        $patterns = @('Unhandled exception')
        $sinceUtc = [datetime]'2026-06-11T09:00:00Z'

        Add-RecentLogLineIfApplicable `
            -Line '2026-06-11T10:00:00Z fail: Unhandled exception' `
            -LineTimestamp $state.CurrentEventUtc `
            -State $state `
            -SinceUtc $sinceUtc `
            -Patterns $patterns `
            -UseFileWriteTimeFallback $false `
            -MatchedLines $matchedLines

        Add-RecentLogLineIfApplicable `
            -Line '   at Contoso.Service.Worker()' `
            -LineTimestamp $null `
            -State $state `
            -SinceUtc $sinceUtc `
            -Patterns $patterns `
            -UseFileWriteTimeFallback $false `
            -MatchedLines $matchedLines

        $matchedLines.Count | Should -Be 2
    }
}

Describe 'Application file copy policy' {
    It 'rejects robocopy /MIR for API targets' {
        { Invoke-RobocopySafe -Source 'C:\src' -Destination 'C:\dst' -TargetType Api -ExtraArguments @('/MIR') } |
            Should -Throw '*API*'
    }

    It 'allows robocopy /MIR for Web targets' {
        $source = New-TempDirectory
        $destination = New-TempDirectory

        Mock robocopy {
            param($args)
            ($args -join ' ') | Should -Match '/MIR'
            $global:LASTEXITCODE = 1
        }

        {
            Invoke-RobocopySafe `
                -Source $source `
                -Destination $destination `
                -TargetType Web `
                -ExtraArguments @('/MIR')
        } | Should -Not -Throw
    }

    It 'copies API and Web without /MIR in default payload flow' {
        $sourceApi = New-TempDirectory
        $sourceWeb = New-TempDirectory
        $targetApi = New-TempDirectory
        $targetWeb = New-TempDirectory

        Set-Content (Join-Path $sourceApi 'Uqeb.Api.dll') 'dll'
        (@{ secret = $true } | ConvertTo-Json -Compress) | Set-Content (Join-Path $sourceApi 'appsettings.Production.json')
        Set-Content (Join-Path $sourceWeb 'index.html') 'html'

        Mock robocopy {
            param($args)
            $joined = ($args -join ' ')
            $joined | Should -Not -Match '/MIR'
            $global:LASTEXITCODE = 1
        }

        Copy-ApplicationPayload -ApiSource $sourceApi -WebSource $sourceWeb -ApiTarget $targetApi -WebTarget $targetWeb
        Test-Path (Join-Path $targetApi 'appsettings.Production.json') | Should -BeFalse
    }

    It 'preserves production appsettings by copying from config path during install flow' {
        $config = New-TempDirectory
        $apiTarget = New-TempDirectory
        $settings = Join-Path $config 'appsettings.Production.json'
        New-TestProductionSettingsJson | Set-Content $settings -Encoding UTF8

        Copy-Item $settings (Join-Path $apiTarget 'appsettings.Production.json') -Force
        (Get-Content (Join-Path $apiTarget 'appsettings.Production.json') -Raw) | Should -Match 'UqebDb'
    }
}

Describe 'Invoke-DeploymentFileRollback' {
    It 'restores API and Web atomically without stale API files' {
        Test-DirectoryHasContent '' | Should -BeFalse
        Test-DirectoryHasContent '   ' | Should -BeFalse
        Test-DirectoryHasContent $null | Should -BeFalse

        $backupApi = New-TempDirectory
        $backupWeb = New-TempDirectory
        $apiTarget = New-TempDirectory
        $webTarget = New-TempDirectory
        Set-Content (Join-Path $backupApi 'Uqeb.Api.dll') 'old-api'
        Set-Content (Join-Path $backupWeb 'index.html') 'old-web'
        Set-Content (Join-Path $apiTarget 'Uqeb.Api.dll') 'new-api'
        Set-Content (Join-Path $apiTarget 'failed-release-only.dll') 'stale'
        Set-Content (Join-Path $webTarget 'index.html') 'new-web'

        $configRoot = New-TempDirectory
        $configSource = Join-Path $configRoot 'appsettings.Production.json'
        $configTarget = Join-Path $apiTarget 'appsettings.Production.json'
        New-TestProductionSettingsJson -ConfigMarker 'approved' | Set-Content $configSource -Encoding UTF8
        Mock-RobocopyAsCopyOnNonWindows

        $restored = Invoke-DeploymentFileRollback `
            -BackupApi $backupApi `
            -BackupWeb $backupWeb `
            -ApiTarget $apiTarget `
            -WebTarget $webTarget `
            -ConfigTarget $configTarget `
            -ConfigSource $configSource

        $restored | Should -BeTrue
        (Get-Content (Join-Path $apiTarget 'Uqeb.Api.dll') -Raw).Trim() | Should -Be 'old-api'
        Test-Path (Join-Path $apiTarget 'failed-release-only.dll') | Should -BeFalse
        (Get-Content (Join-Path $webTarget 'index.html') -Raw).Trim() | Should -Be 'old-web'
        ([string](Get-Content $configTarget -Raw | ConvertFrom-Json).ConfigMarker) | Should -Be 'approved'
    }

    It 'throws before partial restore when ConfigSource is missing' {
        $backupApi = New-TempDirectory
        $backupWeb = New-TempDirectory
        $apiTarget = New-TempDirectory
        $webTarget = New-TempDirectory
        Set-Content (Join-Path $backupApi 'Uqeb.Api.dll') 'old-api'
        Set-Content (Join-Path $backupWeb 'index.html') 'old-web'
        Set-Content (Join-Path $apiTarget 'Uqeb.Api.dll') 'new-api'
        Set-Content (Join-Path $apiTarget 'failed-release-only.dll') 'stale'
        Set-Content (Join-Path $webTarget 'index.html') 'new-web'

        $configSource = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        $configTarget = Join-Path $apiTarget 'appsettings.Production.json'
        Mock-RobocopyAsCopyOnNonWindows

        $errorRecord = $null
        try {
            Invoke-DeploymentFileRollback `
                -BackupApi $backupApi `
                -BackupWeb $backupWeb `
                -ApiTarget $apiTarget `
                -WebTarget $webTarget `
                -ConfigTarget $configTarget `
                -ConfigSource $configSource
        }
        catch {
            $errorRecord = $_
        }

        $errorRecord | Should -Not -BeNullOrEmpty
        $errorRecord.Exception.Message | Should -Match 'إعداد الإنتاج المعتمد غير موجود أثناء file rollback'
        $errorRecord.Exception.Message | Should -Match ([regex]::Escape($configSource))
        (Get-Content (Join-Path $apiTarget 'Uqeb.Api.dll') -Raw).Trim() | Should -Be 'new-api'
        Test-Path (Join-Path $apiTarget 'failed-release-only.dll') | Should -BeTrue
        (Get-Content (Join-Path $webTarget 'index.html') -Raw).Trim() | Should -Be 'new-web'

        {
            Invoke-DeploymentFileRollback `
                -BackupApi $backupApi `
                -BackupWeb $backupWeb `
                -ApiTarget $apiTarget `
                -WebTarget $webTarget `
                -ConfigTarget $configTarget `
                -ConfigSource ''
        } | Should -Throw '*إعداد الإنتاج المعتمد غير موجود أثناء file rollback*'
    }
}

Describe 'Deployment package archival safety' {
    It 'archives a new ZIP and SHA256 pair' {
        $root = New-TempDirectory
        $archive = Join-Path $root 'deployed'
        $zip = Join-Path $root 'Uqeb-test.zip'
        $sha = Join-Path $root 'Uqeb-test.sha256.txt'
        Set-Content $zip 'zip-content' -Encoding ASCII
        Set-Content $sha ('a' * 64) -Encoding ASCII

        $result = Move-DeploymentPackageToArchive -ZipPath $zip -Sha256Path $sha -ArchiveDirectory $archive

        $result.Status | Should -Be 'archived'
        Test-Path (Join-Path $archive 'Uqeb-test.zip') | Should -BeTrue
        Test-Path (Join-Path $archive 'Uqeb-test.sha256.txt') | Should -BeTrue
        Test-Path $zip | Should -BeFalse
        Test-Path $sha | Should -BeFalse
    }

    It 'treats an identical archived pair as already archived without overwrite' {
        $root = New-TempDirectory
        $archive = Join-Path $root 'deployed'
        Ensure-Directory $archive
        $zip = Join-Path $root 'Uqeb-test.zip'
        $sha = Join-Path $root 'Uqeb-test.sha256.txt'
        Set-Content $zip 'zip-content' -Encoding ASCII
        Set-Content $sha ('a' * 64) -Encoding ASCII
        Copy-Item $zip (Join-Path $archive 'Uqeb-test.zip')
        Copy-Item $sha (Join-Path $archive 'Uqeb-test.sha256.txt')

        $result = Move-DeploymentPackageToArchive -ZipPath $zip -Sha256Path $sha -ArchiveDirectory $archive

        $result.Status | Should -Be 'already_archived'
        Test-Path $zip | Should -BeFalse
        Test-Path $sha | Should -BeFalse
    }

    It 'rejects an archived ZIP with the same name and different hash' {
        $root = New-TempDirectory
        $archive = Join-Path $root 'deployed'
        Ensure-Directory $archive
        $zip = Join-Path $root 'Uqeb-test.zip'
        $sha = Join-Path $root 'Uqeb-test.sha256.txt'
        Set-Content $zip 'new-zip' -Encoding ASCII
        Set-Content $sha ('a' * 64) -Encoding ASCII
        Set-Content (Join-Path $archive 'Uqeb-test.zip') 'old-zip' -Encoding ASCII
        Copy-Item $sha (Join-Path $archive 'Uqeb-test.sha256.txt')

        { Move-DeploymentPackageToArchive -ZipPath $zip -Sha256Path $sha -ArchiveDirectory $archive } |
            Should -Throw '*محتوى مختلف*'
    }

    It 'rejects an archived SHA256 sidecar with different content' {
        $root = New-TempDirectory
        $archive = Join-Path $root 'deployed'
        Ensure-Directory $archive
        $zip = Join-Path $root 'Uqeb-test.zip'
        $sha = Join-Path $root 'Uqeb-test.sha256.txt'
        Set-Content $zip 'zip-content' -Encoding ASCII
        Set-Content $sha ('a' * 64) -Encoding ASCII
        Copy-Item $zip (Join-Path $archive 'Uqeb-test.zip')
        Set-Content (Join-Path $archive 'Uqeb-test.sha256.txt') ('b' * 64) -Encoding ASCII

        { Move-DeploymentPackageToArchive -ZipPath $zip -Sha256Path $sha -ArchiveDirectory $archive } |
            Should -Throw '*محتوى مختلف*'
    }

    It 'restores ZIP source when SHA256 move fails' {
        $root = New-TempDirectory
        $archive = Join-Path $root 'deployed'
        $zip = Join-Path $root 'Uqeb-test.zip'
        $sha = Join-Path $root 'Uqeb-test.sha256.txt'
        Set-Content $zip 'zip-content' -Encoding ASCII
        Set-Content $sha ('a' * 64) -Encoding ASCII

        try {
            $script:PackageArchiveFailureInjection = 'before_sha_move'
            { Move-DeploymentPackageToArchive -ZipPath $zip -Sha256Path $sha -ArchiveDirectory $archive } |
                Should -Throw '*simulated SHA move failure*'
        }
        finally {
            $script:PackageArchiveFailureInjection = $null
        }

        Test-Path $zip | Should -BeTrue
        Test-Path $sha | Should -BeTrue
        Test-Path (Join-Path $archive 'Uqeb-test.zip') | Should -BeFalse
    }
}

Describe 'install-production-package.ps1 scenarios' {
    BeforeEach {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock
    }

    It 'does not expose SkipDatabaseBackup parameter' {
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $script:InstallScript,
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
        $paramBlock = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ParamBlockAst] }, $true)[0]
        $names = @($paramBlock.Parameters | ForEach-Object { $_.Name.Extent.Text })
        $names | Should -Not -Contain 'SkipDatabaseBackup'
    }

    It 'keeps ApplyDatabaseMigration only for backward-compatible deprecation' {
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $script:InstallScript,
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
        $paramBlock = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.ParamBlockAst] }, $true)[0]
        $names = @($paramBlock.Parameters | ForEach-Object { $_.Name.Extent.Text })
        $names | Should -Not -Contain '$SkipDatabaseMigration'
        $names | Should -Contain '$ApplyDatabaseMigration'
        $names | Should -Contain '$ApiBindAddress'
    }

    It 'blocks database backup bypass environment variables' {
        { Assert-DatabaseBackupNotBypassed } | Should -Not -Throw
        $previous = $env:UQEB_SKIP_DATABASE_BACKUP
        try {
            $env:UQEB_SKIP_DATABASE_BACKUP = '1'
            { Assert-DatabaseBackupNotBypassed } | Should -Throw
        }
        finally {
            if ($null -eq $previous) {
                Remove-Item Env:UQEB_SKIP_DATABASE_BACKUP -ErrorAction SilentlyContinue
            }
            else {
                $env:UQEB_SKIP_DATABASE_BACKUP = $previous
            }
        }
    }

    It 'rejects folder path instead of ZIP' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        $folder = New-TempDirectory
        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env -AdditionalParameters @{ PackagePath = $folder } } | Should -Throw '*ZIP*'
    }

    It 'stops on automatic migration failure and does not promote' {
        Mock Test-RequiredMigrationPresent { return $false }
        Mock Invoke-RestartCurrentReleaseService {}
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath -PackageMigrationContent 'throw "migration failed"'
        'throw "migration failed"' | Set-Content (Join-Path $env.ToolsRoot 'apply-migrations.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw
        Assert-MockCalled Install-StagedReleaseToProduction -Times 0 -Exactly
        Assert-MockCalled Invoke-RestartCurrentReleaseService -Times 1
    }

    It 'fails deployment when health verification fails' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw
    }

    It 'skips migration execution when required migration is already present' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "migration script must not run"' | Set-Content (Join-Path $env.ToolsRoot 'apply-migrations.ps1') -Encoding ASCII
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }
        $result.Threw | Should -BeFalse
        $result.Text | Should -Match 'Required migration already applied; migration execution skipped.'
        Assert-MockCalled Invoke-ProductionDatabaseBackup -Times 1 -Exactly
        Assert-MockCalled Test-RequiredMigrationPresent -Times 1 -Exactly
        Assert-MockCalled Test-RequiredMigrationApplied -Times 0 -Exactly
        Assert-MockCalled Install-StagedReleaseToProduction -Times 1 -Exactly
    }

    It 'prints deprecation warning when ApplyDatabaseMigration is supplied without changing behavior' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "migration script must not run"' | Set-Content (Join-Path $env.ToolsRoot 'apply-migrations.ps1') -Encoding ASCII
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript `
                -InstallScript $script:InstallScript `
                -Environment $env `
                -AdditionalParameters @{ ApplyDatabaseMigration = $true }
        }

        $result.Threw | Should -BeFalse
        $result.Text | Should -Match 'deprecated and no longer required'
        Assert-MockCalled Test-RequiredMigrationPresent -Times 1 -Exactly
        Assert-MockCalled Test-RequiredMigrationApplied -Times 0 -Exactly
    }

    It 'still applies a missing migration when deprecated ApplyDatabaseMigration is supplied' {
        Mock Test-RequiredMigrationPresent { return $false }
        $global:deprecatedMigrationApplied = $false
        $env = New-InstallTestEnvironment `
            -CommonPath $script:CommonPath `
            -PackageMigrationContent '$global:deprecatedMigrationApplied = $true'
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        Invoke-TestInstallScript `
            -InstallScript $script:InstallScript `
            -Environment $env `
            -AdditionalParameters @{ ApplyDatabaseMigration = $true } | Out-Null

        $global:deprecatedMigrationApplied | Should -BeTrue
        Assert-MockCalled Test-RequiredMigrationApplied -Times 1 -Exactly
    }

    It 'still runs database backup when SkipFileBackup is set' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env -AdditionalParameters @{ SkipFileBackup = $true } } |
            Should -Not -Throw
        Assert-MockCalled Invoke-ProductionDatabaseBackup -Times 1 -Exactly
    }

    It 'does not declare success when API port never opens' {
        Mock Test-PortListener { $false }
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'apply-migrations.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw '*API*'
    }

    It 'stops deployment when database backup fails before stopping API' {
        Mock Invoke-ProductionDatabaseBackup { throw 'backup failed' }
        $global:deployOrder = @()
        Mock Stop-ScheduledTask { $global:deployOrder += 'stop-api' }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw

        $global:deployOrder | Should -Not -Contain 'stop-api'
        Assert-MockCalled Install-StagedReleaseToProduction -Times 0 -Exactly
    }

    It 'runs database backup before API stop and missing migration before promotion' {
        $global:deployOrder = @()
        Mock Invoke-ProductionDatabaseBackup {
            $global:deployOrder += 'db-backup'
            $backupFile = Join-Path $TestDrive ("UqebDb-before-order.bak")
            Set-Content -LiteralPath $backupFile -Value ('x' * 64) -Encoding ASCII
            return [pscustomobject]@{
                Path = $backupFile
                SizeBytes = 64
                CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                Sha256 = ('b' * 64)
                DatabaseName = 'UqebDb'
            }
        }
        Mock Stop-ScheduledTask { $global:deployOrder += 'stop-api' }
        Mock Test-RequiredMigrationPresent {
            $global:deployOrder += 'migration-check'
            return $false
        }
        Mock Test-RequiredMigrationApplied {
            $global:deployOrder += 'migration-verified'
        }
        Mock Install-StagedReleaseToProduction {
            param($InstallRoot, $Version)
            $global:deployOrder += 'promotion'
            return [pscustomobject]@{
                Paths = [pscustomobject]@{
                    ReleaseRoot = Join-Path $InstallRoot ("releases\" + $Version)
                    CurrentApi = Join-Path $InstallRoot 'current\api'
                    CurrentWeb = Join-Path $InstallRoot 'current\web'
                }
                ConfigTarget = Join-Path $InstallRoot 'current\api\appsettings.Production.json'
            }
        }

        $env = New-InstallTestEnvironment `
            -CommonPath $script:CommonPath `
            -PackageMigrationContent '$global:deployOrder += "migration"'
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        Invoke-TestInstallScript `
            -InstallScript $script:InstallScript `
            -Environment $env | Out-Null

        $global:deployOrder[0] | Should -Be 'db-backup'
        ($global:deployOrder.IndexOf('db-backup') -lt $global:deployOrder.IndexOf('migration-check')) | Should -BeTrue
        ($global:deployOrder.IndexOf('db-backup') -lt $global:deployOrder.IndexOf('stop-api')) | Should -BeTrue
        ($global:deployOrder.IndexOf('stop-api') -lt $global:deployOrder.IndexOf('migration')) | Should -BeTrue
        ($global:deployOrder.IndexOf('migration') -lt $global:deployOrder.IndexOf('migration-verified')) | Should -BeTrue
        ($global:deployOrder.IndexOf('migration-verified') -lt $global:deployOrder.IndexOf('promotion')) | Should -BeTrue
        Test-Path (Join-Path $env.InstallRoot 'incoming\deployed\Uqeb-test.zip') | Should -BeTrue
    }

    It 'does not interpret migration verification infrastructure failure as missing migration' {
        Mock Test-RequiredMigrationPresent { throw 'SQL timeout while reading history' }
        $global:deployOrder = @()
        Mock Stop-ScheduledTask { $global:deployOrder += 'stop-api' }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw '*SQL timeout while reading history*'

        $global:deployOrder | Should -Not -Contain 'stop-api'
        Assert-MockCalled Invoke-ProductionDatabaseBackup -Times 1 -Exactly
        Assert-MockCalled Test-RequiredMigrationApplied -Times 0 -Exactly
        Assert-MockCalled Install-StagedReleaseToProduction -Times 0 -Exactly
    }

    It 'fails before promotion when migration remains missing after apply' {
        Mock Test-RequiredMigrationPresent { return $false }
        Mock Test-RequiredMigrationApplied { throw 'required migration still missing' }
        Mock Invoke-RestartCurrentReleaseService {}
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } |
            Should -Throw '*required migration still missing*'

        Assert-MockCalled Install-StagedReleaseToProduction -Times 0 -Exactly
        Assert-MockCalled Invoke-RestartCurrentReleaseService -Times 1
    }

    It 'shows manual restore command when a later deployment step fails' {
        $backupFile = Join-Path $TestDrive 'UqebDb-before-restore.bak'
        Set-Content -LiteralPath $backupFile -Value ('x' * 64) -Encoding ASCII
        Mock Invoke-ProductionDatabaseBackup {
            return [pscustomobject]@{
                Path = $backupFile
                SizeBytes = 64
                CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                Sha256 = ('c' * 64)
                DatabaseName = 'UqebDb'
            }
        }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $output = @()
        try {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env *>&1 |
                ForEach-Object { $output += $_.ToString() }
        }
        catch {
            $output += $_.Exception.Message
        }

        ($output -join [Environment]::NewLine) | Should -Match 'RESTORE DATABASE'
    }

    It 'succeeds when stale LASTEXITCODE is set before optional migration' {
        $global:LASTEXITCODE = 1

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env -AdditionalParameters @{ ApiPort = 5001 } } | Should -Not -Throw
    }

    It 'does not move package to deployed when deployment fails' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        { Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env } | Should -Throw
        Test-Path (Join-Path $env.InstallRoot 'incoming\deployed\Uqeb-test.zip') | Should -BeFalse
        Test-Path $env.ZipPath | Should -BeTrue
    }

    It 'reports archival failure without rolling back a healthy published release' {
        Mock Move-DeploymentPackageToArchive { throw 'archive collision' }
        Mock Invoke-ReleaseRollbackFromState { throw 'release rollback must not run' }
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Threw | Should -BeFalse
        $result.Text | Should -Match 'فشلت أرشفة حزمة ZIP/SHA256'
        $result.Text | Should -Match 'حالة أرشفة الحزمة: فشلت'
        Assert-MockCalled Invoke-ReleaseRollbackFromState -Times 0 -Exactly
    }

    It 'passes production bind address to run-api writer while run-api content is all-interface bound' {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env | Out-Null

        $runScriptPath = Join-Path $env.InstallRoot 'run-api.cmd'
        $runScript = Get-Content -LiteralPath $runScriptPath -Raw
        $runScript | Should -Match 'ASPNETCORE_URLS=http://0\.0\.0\.0:5000'
        $runScript | Should -Not -Match 'ASPNETCORE_URLS=http://10\.0\.177\.17:5000'
    }

    It 'derives ApiBaseUrl from ApiBindAddress and ApiPort when URL is omitted' {
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $script:InstallScript,
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
        $content = Get-Content -LiteralPath $script:InstallScript -Raw
        $content | Should -Match 'IsNullOrWhiteSpace\(\$ApiBaseUrl\)'
        $content | Should -Match 'http://\$\{ApiBindAddress\}:\$ApiPort'
        $content | Should -Not -Match 'http://localhost:\$ApiPort'
    }

    It 'rejects invalid ApiBindAddress values' {
        { Assert-ValidApiBindAddress -ApiBindAddress 'http://10.0.177.17' } | Should -Throw
        { Assert-ValidApiBindAddress -ApiBindAddress '0.0.0.0:5000' } | Should -Throw
        { Assert-ValidApiBindAddress -ApiBindAddress '127.0.0.1' } | Should -Throw '*loopback*'
        { Assert-ValidApiBindAddress -ApiBindAddress '0.0.0.0' } | Should -Not -Throw
        { Assert-ValidApiBindAddress -ApiBindAddress '10.0.177.17' } | Should -Not -Throw
    }

    It 'Write-DeployFailure does not terminate catch cleanup under Stop preference' {
        $ErrorActionPreference = 'Stop'
        $output = @(Write-DeployFailure 'visible failure message')
        ($output -join [Environment]::NewLine) | Should -Match '\[خطأ\].*visible failure message'

        $continued = $false
        try {
            throw 'simulate failure'
        }
        catch {
            Write-DeployFailure $_.Exception.Message
            $continued = $true
        }

        $continued | Should -BeTrue
    }

    It 'Write-DeployError does not terminate rollback cleanup under Stop preference' {
        $ErrorActionPreference = 'Stop'
        $continued = $false
        $rollbackStepReached = $false

        try {
            throw 'simulate promotion failure'
        }
        catch {
            Write-DeployError ('deploy-failed: ' + $_.Exception.Message)
            $continued = $true
            Write-DeployInfo 'simulated rollback step after error log'
            $rollbackStepReached = $true
        }

        $continued | Should -BeTrue
        $rollbackStepReached | Should -BeTrue
    }

    It 'continues catch with service restart and final report after stop-before-promotion failure' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Wait-PortReleased { $false }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env -AdditionalParameters @{ SkipFileBackup = $true }
        }

        $result.Text | Should -Match '\[خطأ\]'
        $result.Text | Should -Match 'تقرير النشر النهائي'
        Assert-MockCalled Invoke-RestartCurrentReleaseService -Times 1 -Exactly
    }

    It 'continues catch with release rollback and final report after health failure' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match '\[خطأ\]'
        $result.Text | Should -Match 'تقرير النشر النهائي'
        Assert-MockCalled Invoke-ReleaseRollbackFromState -Times 1 -Exactly -ParameterFilter {
            $ConfigPath -eq $env.ConfigPath
        }
    }

    It 'continues catch with restart attempt and final report after promotion failure' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Install-StagedReleaseToProduction { throw 'promotion failed during install' }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match '\[خطأ\].*promotion failed'
        $result.Text | Should -Match 'تقرير النشر النهائي'
        Assert-MockCalled Invoke-RestartCurrentReleaseService -Times 1 -Exactly
    }

    It 'uses approved ConfigPath during release rollback with SkipFileBackup' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env -AdditionalParameters @{ SkipFileBackup = $true }
        } | Should -Throw

        Assert-MockCalled Invoke-ReleaseRollbackFromState -Times 1 -Exactly -ParameterFilter {
            $ConfigPath -eq $env.ConfigPath
        }
        $env.ConfigPath | Should -Be (Join-Path $env.InstallRoot 'config\appsettings.Production.json')
    }
}

Describe 'Release promotion and rollback state' {
    BeforeEach {
        if (-not (Get-Command Stop-ScheduledTask -ErrorAction SilentlyContinue)) {
            function script:Stop-ScheduledTask { param([string]$TaskName) }
        }
        if (-not (Get-Command Start-ScheduledTask -ErrorAction SilentlyContinue)) {
            function script:Start-ScheduledTask { param([string]$TaskName) }
        }
    }

    It 'writes and reads rollback-state.json' {
        $root = New-TempDirectory
        $state = [ordered]@{
            updatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            currentRelease = '20260101-120000'
            previousRelease = '20251231-120000'
            package = 'Uqeb-test.zip'
            commitSha = 'abc'
        }

        Write-RollbackState -InstallRoot $root -State $state
        $loaded = Read-RollbackState -InstallRoot $root

        $loaded.currentRelease | Should -Be '20260101-120000'
        $loaded.previousRelease | Should -Be '20251231-120000'
    }

    It 'returns false when rollback state has no previous release' {
        Mock Stop-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }

        $root = New-TempDirectory
        Write-RollbackState -InstallRoot $root -State ([ordered]@{
            currentRelease = 'only'
            previousRelease = ''
        })

        $result = @(Invoke-ReleaseRollbackFromState `
            -InstallRoot $root `
            -TaskName 'UqebApi' `
            -ApiPort 5000)[-1]

        $result | Should -BeFalse
    }
}

Describe 'Production database backup functions' {
    BeforeAll {
        $script:TestConnectionString = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
    }

    AfterEach {
        $global:SqlDeploymentCommandHandler = $null
    }

    It 'invokes global SQL handler' {
        $script:SqlHandlerReceivedConnectionString = $true
        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            $script:SqlHandlerReceivedConnectionString = $PSBoundParameters.ContainsKey('ConnectionString')
            return "handled:$CommandText"
        }

        Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -CommandText 'PING' | Should -Be 'handled:PING'
        $script:SqlHandlerReceivedConnectionString | Should -BeFalse
    }

    It 'passes ConnectionString to SQL handler only when explicitly supported' {
        $script:SqlHandlerReceivedConnectionString = ''
        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$ConnectionString,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            $script:SqlHandlerReceivedConnectionString = $ConnectionString
            return "handled:$CommandText"
        }

        Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -ConnectionString $script:TestConnectionString -CommandText 'PING' | Should -Be 'handled:PING'
        $script:SqlHandlerReceivedConnectionString | Should -Be $script:TestConnectionString
    }

    It 'passes ConnectionString to command-based SQL handler when explicitly supported' {
        try {
            $global:UqebCommandSqlHandlerConnectionString = ''
            function global:Invoke-UqebCommandSqlHandlerWithConnectionString {
                param(
                    [string]$Server,
                    [string]$Database,
                    [string]$ConnectionString,
                    [string]$CommandText,
                    [bool]$Scalar,
                    [bool]$DataTable
                )

                $global:UqebCommandSqlHandlerConnectionString = $ConnectionString
                return "handled:$CommandText"
            }

            $global:SqlDeploymentCommandHandler = 'Invoke-UqebCommandSqlHandlerWithConnectionString'

            Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -ConnectionString $script:TestConnectionString -CommandText 'PING' | Should -Be 'handled:PING'
            $global:UqebCommandSqlHandlerConnectionString | Should -Be $script:TestConnectionString
        }
        finally {
            Remove-Item -LiteralPath 'Function:\Invoke-UqebCommandSqlHandlerWithConnectionString' -ErrorAction SilentlyContinue
            Remove-Variable -Name UqebCommandSqlHandlerConnectionString -Scope Global -ErrorAction SilentlyContinue
        }
    }

    It 'does not pass ConnectionString to command-based SQL handler unless supported' {
        try {
            $global:UqebCommandSqlHandlerReceivedConnectionString = $true
            function global:Invoke-UqebCommandSqlHandlerWithoutConnectionString {
                param(
                    [string]$Server,
                    [string]$Database,
                    [string]$CommandText,
                    [bool]$Scalar,
                    [bool]$DataTable
                )

                $global:UqebCommandSqlHandlerReceivedConnectionString = $PSBoundParameters.ContainsKey('ConnectionString')
                return "handled:$CommandText"
            }

            $global:SqlDeploymentCommandHandler = 'Invoke-UqebCommandSqlHandlerWithoutConnectionString'

            Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -ConnectionString $script:TestConnectionString -CommandText 'PING' | Should -Be 'handled:PING'
            $global:UqebCommandSqlHandlerReceivedConnectionString | Should -BeFalse
        }
        finally {
            Remove-Item -LiteralPath 'Function:\Invoke-UqebCommandSqlHandlerWithoutConnectionString' -ErrorAction SilentlyContinue
            Remove-Variable -Name UqebCommandSqlHandlerReceivedConnectionString -Scope Global -ErrorAction SilentlyContinue
        }
    }

    It 'returns DataTable from SQL test handler' {
        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            $table = New-Object System.Data.DataTable
            [void]$table.Columns.Add('DatabaseName', [string])
            $row = $table.NewRow()
            $row['DatabaseName'] = 'UqebDb'
            [void]$table.Rows.Add($row)
            return $table
        }

        $header = Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -CommandText 'RESTORE HEADERONLY FROM DISK = N''x'';' -DataTable
        if ($header -is [System.Data.DataRow]) {
            $header = $header.Table
        }
        ($header -is [System.Data.DataTable]) | Should -BeTrue
        $header.Rows.Count | Should -Be 1
        [string]$header.Rows[0]['DatabaseName'] | Should -Be 'UqebDb'
    }

    It 'executes BACKUP DATABASE WITH CHECKSUM and verifies backup' {
        $backupDir = New-TempDirectory
        $backupPath = Join-Path $backupDir 'UqebDb-before-sql-test.bak'

        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )

            if ($CommandText -like 'BACKUP DATABASE*WITH CHECKSUM*') {
                Set-Content -LiteralPath $backupPath -Value ('x' * 128) -Encoding ASCII
                return $null
            }
            if ($CommandText -like 'RESTORE VERIFYONLY*WITH CHECKSUM*') {
                return $null
            }
            if ($CommandText -like 'RESTORE HEADERONLY*') {
                $table = New-Object System.Data.DataTable
                [void]$table.Columns.Add('DatabaseName', [string])
                $row = $table.NewRow()
                $row['DatabaseName'] = 'UqebDb'
                [void]$table.Rows.Add($row)
                return $table
            }

            throw "Unexpected SQL: $CommandText"
        }

        $result = Invoke-ProductionDatabaseBackup `
            -Server '.' `
            -Database 'UqebDb' `
            -ConnectionString $script:TestConnectionString `
            -BackupDirectory $backupDir `
            -Timestamp 'sql-test'

        $result.Path | Should -Be $backupPath
        $result.SizeBytes | Should -BeGreaterThan 0
        $result.Sha256 | Should -Not -BeNullOrEmpty
    }

    It 'fails when BACKUP DATABASE command fails' {
        $global:SqlDeploymentCommandHandler = { throw 'backup error' }

        { Invoke-ProductionDatabaseBackup -Server '.' -Database 'UqebDb' -ConnectionString $script:TestConnectionString -BackupDirectory (New-TempDirectory) -Timestamp 'fail' } |
            Should -Throw '*BACKUP DATABASE*'
    }

    It 'fails when RESTORE VERIFYONLY fails' {
        $backupDir = New-TempDirectory
        $backupPath = Join-Path $backupDir 'UqebDb-before-verifyfail.bak'

        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            if ($CommandText -like 'BACKUP DATABASE*') {
                Set-Content -LiteralPath $backupPath -Value ('x' * 64) -Encoding ASCII
                return $null
            }
            if ($CommandText -like 'RESTORE VERIFYONLY*') { throw 'verify failed' }
            return $null
        }

        { Invoke-ProductionDatabaseBackup -Server '.' -Database 'UqebDb' -ConnectionString $script:TestConnectionString -BackupDirectory $backupDir -Timestamp 'verifyfail' } |
            Should -Throw '*RESTORE VERIFYONLY*'
    }

    It 'fails when backup file size is zero' {
        $backupDir = New-TempDirectory
        $backupPath = Join-Path $backupDir 'UqebDb-before-zero.bak'
        Set-Content -LiteralPath $backupPath -Value '' -Encoding ASCII

        { Confirm-ProductionDatabaseBackupFile -Server '.' -Database 'UqebDb' -BackupPath $backupPath } |
            Should -Throw
    }

    It 'creates backup directory when missing' {
        $backupRoot = New-TempDirectory
        $backupDir = Join-Path $backupRoot 'db'
        Test-Path -LiteralPath $backupDir | Should -BeFalse

        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            if ($CommandText -like 'BACKUP DATABASE*') {
                if ($CommandText -match "DISK = N'([^']+)'") {
                    Set-Content -LiteralPath $Matches[1] -Value ('x' * 32) -Encoding ASCII
                }
                return $null
            }
            if ($CommandText -like 'RESTORE VERIFYONLY*') { return $null }
            if ($CommandText -like 'RESTORE HEADERONLY*') {
                $table = New-Object System.Data.DataTable
                [void]$table.Columns.Add('DatabaseName', [string])
                $row = $table.NewRow()
                $row['DatabaseName'] = 'UqebDb'
                [void]$table.Rows.Add($row)
                return $table
            }
            throw "Unexpected SQL: $CommandText"
        }

        Invoke-ProductionDatabaseBackup -Server '.' -Database 'UqebDb' -ConnectionString $script:TestConnectionString -BackupDirectory $backupDir -Timestamp 'mkdir' | Out-Null
        Test-Path -LiteralPath $backupDir | Should -BeTrue
    }

    It 'uses WITH CHECKSUM in backup and verify commands' {
        $backupDir = New-TempDirectory
        $commands = New-Object System.Collections.Generic.List[string]

        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            $commands.Add([string]$CommandText) | Out-Null
            if ($CommandText -like 'BACKUP DATABASE*') {
                if ($CommandText -match "DISK = N'([^']+)'") {
                    Set-Content -LiteralPath $Matches[1] -Value ('x' * 32) -Encoding ASCII
                }
                return $null
            }
            if ($CommandText -like 'RESTORE VERIFYONLY*') { return $null }
            if ($CommandText -like 'RESTORE HEADERONLY*') {
                $table = New-Object System.Data.DataTable
                [void]$table.Columns.Add('DatabaseName', [string])
                $row = $table.NewRow()
                $row['DatabaseName'] = 'UqebDb'
                [void]$table.Rows.Add($row)
                return $table
            }
            return $null
        }

        Invoke-ProductionDatabaseBackup -Server '.' -Database 'UqebDb' -ConnectionString $script:TestConnectionString -BackupDirectory $backupDir -Timestamp 'checksum' | Out-Null
        ($commands | Where-Object { $_ -like 'BACKUP DATABASE*' } | Select-Object -First 1) | Should -Match 'WITH CHECKSUM'
        ($commands | Where-Object { $_ -like 'RESTORE VERIFYONLY*' } | Select-Object -First 1) | Should -Match 'WITH CHECKSUM'
    }

    It 'applies retention policy without deleting the newest successful backup' {
        $installRoot = New-TempDirectory
        $backupDir = Join-Path $installRoot 'backup\db'
        Ensure-Directory $backupDir
        $files = New-TestBackupFileSet -BackupDir $backupDir -Count 12

        $deleted = Invoke-DatabaseBackupRetentionPolicy `
            -BackupDirectory $backupDir `
            -InstallRoot $installRoot `
            -MinimumKeepCount 10 `
            -LatestSuccessfulBackupPath $files[0]

        $deleted.Count | Should -Be 2
        Test-Path -LiteralPath $files[0] | Should -BeTrue
        (Get-ChildItem -LiteralPath $backupDir -Filter '*.bak').Count | Should -Be 10
    }
}

Describe 'Protected database backup paths' {
    It 'returns flat string paths from release manifests' {
        $installRoot = New-TempDirectory
        $protectedPath = 'C:\Uqeb\backup\db\UqebDb-before-protected.bak'
        Write-TestReleaseManifestWithBackup -InstallRoot $installRoot -BackupPath $protectedPath

        $paths = @(Get-ProtectedDatabaseBackupPaths -InstallRoot $installRoot)
        @($paths).Count | Should -Be 1
        [string]$paths[0] | Should -Be $protectedPath
    }

    It 'does not delete protected backup paths during retention' {
        $installRoot = New-TempDirectory
        $backupDir = Join-Path $installRoot 'backup\db'
        Ensure-Directory $backupDir

        $protectedPath = Join-Path $backupDir 'UqebDb-before-protected.bak'
        $files = New-TestBackupFileSet -BackupDir $backupDir -Count 12
        Set-Content -LiteralPath $protectedPath -Value ('x' * 10) -Encoding ASCII
        (Get-Item -LiteralPath $protectedPath).LastWriteTime = (Get-Date).AddHours(-20)

        Write-TestReleaseManifestWithBackup -InstallRoot $installRoot -BackupPath $protectedPath

        $deleted = Invoke-DatabaseBackupRetentionPolicy `
            -BackupDirectory $backupDir `
            -InstallRoot $installRoot `
            -MinimumKeepCount 10 `
            -LatestSuccessfulBackupPath $files[0]

        $deleted | Should -Not -Contain $protectedPath
        Test-Path -LiteralPath $protectedPath | Should -BeTrue
    }

    It 'compares protected paths case-insensitively' {
        Test-BackupPathIsProtected `
            -CandidatePath 'C:\Uqeb\backup\db\file.bak' `
            -ProtectedPaths @('c:\uqeb\backup\db\file.bak') | Should -BeTrue
    }
}

Describe 'SQL connection settings' {
    It 'preserves Integrated Security from production settings' {
        $settings = New-TestProductionSettings -ConnectionString (New-TestSqlConnectionString -AuthenticationMode Integrated)
        $info = Get-SqlConnectionInfoFromSettings -SettingsPath $settings
        $builder = Get-TestSqlConnectionBuilder -ConnectionString $info.ConnectionString
        $builder.IntegratedSecurity | Should -BeTrue
    }

    It 'preserves SQL login authentication from production settings' {
        $testPassword = New-TestDatabasePassword
        $connectionString = New-TestSqlConnectionString -AuthenticationMode SqlLogin -Password $testPassword
        $settings = New-TestProductionSettings -ConnectionString $connectionString

        $info = Get-SqlConnectionInfoFromSettings -SettingsPath $settings
        $builder = Get-TestSqlConnectionBuilder -ConnectionString $info.ConnectionString
        $builder.UserID | Should -Be 'uqeb'
        $builder.Password | Should -Be $testPassword
    }

    It 'switches database to master without changing authentication' {
        $testPassword = New-TestDatabasePassword
        $connectionString = New-TestSqlConnectionString -AuthenticationMode SqlLogin -Password $testPassword
        $connection = New-SqlDeploymentConnection -ConnectionString $connectionString -Database 'master'
        $builder = Get-TestSqlConnectionBuilder -ConnectionString $connection.ConnectionString
        if ($builder.ContainsKey('Database')) {
            $builder.Database | Should -Be 'master'
        }
        else {
            $builder['Initial Catalog'] | Should -Be 'master'
        }
        $builder.UserID | Should -Be 'uqeb'
        $builder.Password | Should -Be $testPassword
        $connection.Dispose()
    }

    It 'does not print passwords in redacted connection labels' {
        $label = Get-SqlRedactedConnectionLabel -Server '.' -Database 'UqebDb'
        $label | Should -Not -Match 'Password'
        $label | Should -Match 'Server=\.'
    }

    It 'fails clearly when connection string is missing' {
        $settingsPath = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        (@{ ConnectionStrings = @{} } | ConvertTo-Json -Compress) | Set-Content $settingsPath -Encoding UTF8
        { Get-SqlConnectionInfoFromSettings -SettingsPath $settingsPath } | Should -Throw '*DefaultConnection*'
    }
}

Describe 'apply-migrations.ps1 encoding' {
    It 'reads migration SQL as UTF-8 without corrupting Unicode text' {
        $migration = Join-Path (New-TempDirectory) 'migrations-idempotent.sql'
        $arabicBytes = [byte[]](0xD9, 0x82, 0xD8, 0xB3, 0xD9, 0x85, 0x20, 0xD8, 0xA7, 0xD9, 0x84, 0xD9, 0x85, 0xD8, 0xA7, 0xD9, 0x84, 0xD9, 0x8A, 0xD8, 0xA9)
        $arabicText = [System.Text.Encoding]::UTF8.GetString($arabicBytes)
        $sqlText = "SELECT N'$arabicText';"
        Set-Content -LiteralPath $migration -Value $sqlText -Encoding UTF8
        $sql = Get-Content -LiteralPath $migration -Raw -Encoding UTF8
        $sql | Should -Match $arabicText
    }
}

Describe 'Publish directory junction safety' {
    It 'removes junction without deleting the target directory on Windows' -Skip:([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        $installRoot = New-TempDirectory
        $target = Join-Path $installRoot 'current\api (junction test)'
        $link = Join-Path $installRoot 'publish\api'
        Ensure-Directory $target
        Set-Content (Join-Path $target 'marker.txt') 'keep-me' -Encoding ASCII

        Set-PublishDirectoryJunction -LinkPath $link -TargetPath $target -InstallRoot $installRoot
        Remove-ReparsePointSafe -Path $link

        Test-Path -LiteralPath $target | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $target 'marker.txt') | Should -BeTrue
        Test-Path -LiteralPath $link | Should -BeFalse
    }

    It 'rejects junction targets outside InstallRoot' {
        $installRoot = New-TempDirectory
        $outside = New-TempDirectory
        $link = Join-Path $installRoot 'publish\api'

        { Set-PublishDirectoryJunction -LinkPath $link -TargetPath $outside -InstallRoot $installRoot } |
            Should -Throw '*InstallRoot*'
    }

    It 'verifies junction target matches expected path on Windows' -Skip:([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        $installRoot = New-TempDirectory
        $target = Join-Path $installRoot 'current\api'
        $link = Join-Path $installRoot 'publish\api'
        Ensure-Directory $target
        Set-Content (Join-Path $target 'marker.txt') 'junction-target' -Encoding ASCII

        Set-PublishDirectoryJunction -LinkPath $link -TargetPath $target -InstallRoot $installRoot
        { Assert-JunctionPointsToTarget -LinkPath $link -ExpectedTargetPath $target -InstallRoot $installRoot } | Should -Not -Throw
        Get-NormalizedFullPath -Path (Get-JunctionTargetPath -LinkPath $link) |
            Should -Be (Get-NormalizedFullPath -Path $target)
    }

    It 'removes junction and throws when target does not match on Windows' -Skip:([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        $installRoot = New-TempDirectory
        $expectedTarget = Join-Path $installRoot 'current\api'
        $wrongTarget = Join-Path $installRoot 'wrong\api'
        $link = Join-Path $installRoot 'publish\api'
        Ensure-Directory $expectedTarget
        Ensure-Directory $wrongTarget
        Set-Content (Join-Path $wrongTarget 'marker.txt') 'wrong' -Encoding ASCII

        New-Item -ItemType Junction -Path $link -Target $wrongTarget -Force | Out-Null
        { Assert-JunctionPointsToTarget -LinkPath $link -ExpectedTargetPath $expectedTarget -InstallRoot $installRoot } |
            Should -Throw '*mismatch*'
        Test-Path -LiteralPath $link | Should -BeFalse
        Test-Path -LiteralPath $wrongTarget | Should -BeTrue
    }

    It 'removes broken junction entry without deleting unrelated targets on Windows' -Skip:([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        $installRoot = New-TempDirectory
        $target = Join-Path $installRoot 'current\api (مسار Junction)'
        $survivor = Join-Path $installRoot 'current\api-survivor (backup)'
        $link = Join-Path $installRoot 'publish\api'
        Ensure-Directory $target
        Ensure-Directory $survivor
        Set-Content (Join-Path $target 'marker.txt') 'target-keep' -Encoding ASCII
        Set-Content (Join-Path $survivor 'marker.txt') 'survivor-keep' -Encoding ASCII

        Set-PublishDirectoryJunction -LinkPath $link -TargetPath $target -InstallRoot $installRoot
        Remove-Item -LiteralPath $target -Recurse -Force

        Test-LinkEntryExists -LinkPath $link | Should -BeTrue
        Test-Path -LiteralPath $survivor | Should -BeTrue

        Remove-ReparsePointSafe -Path $link

        Test-LinkEntryExists -LinkPath $link | Should -BeFalse
        Test-Path -LiteralPath $survivor | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $survivor 'marker.txt') | Should -BeTrue

        Set-PublishDirectoryJunction -LinkPath $link -TargetPath $survivor -InstallRoot $installRoot
        Test-LinkEntryExists -LinkPath $link | Should -BeTrue
        (Get-Content -LiteralPath (Join-Path $survivor 'marker.txt') -Raw).Trim() | Should -Be 'survivor-keep'
    }
}

Describe 'Release promotion safety' {
    BeforeEach {
        $script:ReleasePromotionFailureInjection = $null
        Mock-RobocopyAsCopyOnNonWindows
    }

    AfterEach {
        $script:ReleasePromotionFailureInjection = $null
    }

    It 'rejects path traversal in release version' {
        $installRoot = New-TempDirectory
        $staging = Initialize-PromotionFixture -Root $installRoot -Version 'bad' -ApiMarker 'x'
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot

        {
            Install-StagedReleaseToProduction `
                -StagingPath $staging `
                -InstallRoot $installRoot `
                -Version '..\evil' `
                -ConfigPath $configPath `
                -PackagePath 'Uqeb-test.zip' `
                -PackageCommit 'testsha'
        } | Should -Throw '*invalid*'
    }

    It 'rejects installing an immutable release version twice' {
        $installRoot = New-TempDirectory
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot
        $v1Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260101-120000' -ApiMarker 'v1'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath

        {
            Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath
        } | Should -Throw '*already exists*'
    }

    It 'does not leave stale API files in immutable release or current' {
        $installRoot = New-TempDirectory
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot
        $v1Staging = Initialize-PromotionFixture `
            -Root $installRoot `
            -Version '20260101-120000' `
            -ApiMarker 'v1' `
            -ExtraApiFile 'stale-old.dll'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath

        $v2Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260102-120000' -ApiMarker 'v2'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v2Staging -Version '20260102-120000' -ConfigPath $configPath

        Test-Path -LiteralPath (Join-Path $installRoot 'releases\20260102-120000\api\stale-old.dll') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installRoot 'current\api\stale-old.dll') | Should -BeFalse
        Get-CurrentApiMarker -InstallRoot $installRoot | Should -Be 'v2'
    }

    It 'uses authoritative ConfigPath during release rollback' {
        Mock Stop-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }
        Mock Test-PortListener { $true }

        $installRoot = New-TempDirectory
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot -ConfigMarker 'authoritative-config'
        $configHash = Get-FileSha256Hex -Path $configPath

        $v1Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260101-120000' -ApiMarker 'v1'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath

        $v2Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260102-120000' -ApiMarker 'v2'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v2Staging -Version '20260102-120000' -ConfigPath $configPath

        $healthScript = Join-Path $installRoot 'verify-rollback-health.ps1'
        'Write-Output "rollback health passed"' | Set-Content $healthScript -Encoding ASCII
        $rolledBack = Invoke-ReleaseRollbackFromState `
            -InstallRoot $installRoot `
            -TaskName 'UqebApi' `
            -ApiPort 5000 `
            -ConfigPath $configPath `
            -ApiBaseUrl 'http://10.0.177.17:5000' `
            -HealthScriptPath $healthScript `
            -RequireHealthVerification
        $rolledBack | Should -BeTrue

        $rolledBackConfig = Join-Path $installRoot 'current\api\appsettings.Production.json'
        Get-FileSha256Hex -Path $rolledBackConfig | Should -Be $configHash
        ([string](Get-Content -LiteralPath $rolledBackConfig -Raw | ConvertFrom-Json).ConfigMarker) |
            Should -Be 'authoritative-config'

        $state = Read-RollbackState -InstallRoot $installRoot
        $state.currentRelease | Should -Be '20260101-120000'
    }

    It 'rolls back current to previous release on injected promotion failure' -TestCases @(
        @{ Point = 'after_api_swap'; Label = 'after API swap and before Web' }
        @{ Point = 'after_web_swap'; Label = 'after Web swap and before config' }
        @{ Point = 'after_config_copy'; Label = 'after config and before publish links' }
        @{ Point = 'after_publish_links'; Label = 'after publish links and before rollback state' }
        @{ Point = 'before_rollback_state'; Label = 'before rollback state write' }
    ) {
        param($Point, $Label)

        $installRoot = New-TempDirectory
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot
        $v1Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260101-120000' -ApiMarker 'v1-marker'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath

        $v2Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260102-120000' -ApiMarker 'v2-marker'
        $script:ReleasePromotionFailureInjection = $Point

        {
            Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v2Staging -Version '20260102-120000' -ConfigPath $configPath
        } | Should -Throw '*injection*'

        Get-CurrentApiMarker -InstallRoot $installRoot | Should -Be 'v1-marker'
        Get-CurrentWebMarker -InstallRoot $installRoot | Should -Be 'v1-marker'

        $state = Read-RollbackState -InstallRoot $installRoot
        if ($null -ne $state) {
            $state.currentRelease | Should -Be '20260101-120000'
        }
    }
}

Describe 'install-production-package rollback and service recovery' {
    BeforeEach {
        Register-StandardDeploymentInstallMocks
    }

    It 'uses ConfigPath for Invoke-ReleaseRollbackFromState' {
        $content = Get-Content $script:InstallScript -Raw
        $match = [regex]::Match($content, 'Invoke-ReleaseRollbackFromState[\s\S]*?-ConfigPath\s+\$ConfigPath')
        $match.Success | Should -BeTrue
    }

    It 'uses Write-DeployFailure in installer catch paths' {
        $content = Get-Content $script:InstallScript -Raw
        $content | Should -Match 'Write-DeployFailure'
        $content | Should -Not -Match 'catch\s*\{[\s\S]*Write-DeployError'
    }

    It 'Invoke-RestartCurrentReleaseService starts scheduled task and verifies port listener' {
        $global:UqebTestStartCalls = 0
        Mock Start-ScheduledTask {
            $global:UqebTestStartCalls++
        }
        Mock Test-PortListener { $true }

        Invoke-RestartCurrentReleaseService `
            -TaskName 'UqebApi' `
            -ApiPort 5000 `
            -SkipPlaywrightProcessSmokeTest

        $global:UqebTestStartCalls | Should -Be 1
    }

    It 'requires health script and ApiBaseUrl when rollback health verification is mandatory' {
        Mock Start-ScheduledTask {}
        Mock Test-PortListener { $true }

        {
            Invoke-RestartCurrentReleaseService `
                -TaskName 'UqebApi' `
                -ApiPort 5000 `
                -RequireHealthVerification
        } | Should -Throw '*ApiBaseUrl is required*'

        {
            Invoke-RestartCurrentReleaseService `
                -TaskName 'UqebApi' `
                -ApiPort 5000 `
                -ApiBaseUrl 'http://10.0.177.17:5000' `
                -HealthScriptPath (Join-Path $TestDrive 'missing-health.ps1') `
                -RequireHealthVerification
        } | Should -Throw '*required but missing*'
        Assert-MockCalled Start-ScheduledTask -Times 0 -Exactly
    }

    It 'fails mandatory rollback verification when health script fails' {
        Mock Start-ScheduledTask {}
        Mock Test-PortListener { $true }
        $healthScript = Join-Path $TestDrive 'health-fail.ps1'
        'throw "database check failed"' | Set-Content $healthScript -Encoding ASCII

        {
            Invoke-RestartCurrentReleaseService `
                -TaskName 'UqebApi' `
                -ApiPort 5000 `
                -ApiBaseUrl 'http://10.0.177.17:5000' `
                -HealthScriptPath $healthScript `
                -RequireHealthVerification
        } | Should -Throw '*database check failed*'
    }

    It 'passes mandatory rollback verification only after port and health succeed' {
        Mock Start-ScheduledTask {}
        Mock Test-PortListener { $true }
        $healthScript = Join-Path $TestDrive 'health-pass.ps1'
        'Write-Output "health-pass"' | Set-Content $healthScript -Encoding ASCII

        {
            Invoke-RestartCurrentReleaseService `
                -TaskName 'UqebApi' `
                -ApiPort 5000 `
                -ApiBaseUrl 'http://10.0.177.17:5000' `
                -HealthScriptPath $healthScript `
                -RequireHealthVerification
        } | Should -Not -Throw
    }

    It 'does not report release rollback success when the restored API fails verification' {
        Mock Start-ScheduledTask {}
        Mock Stop-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }
        Mock Test-PortListener { $false }

        $installRoot = New-TempDirectory
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot
        $v1Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260101-120000' -ApiMarker 'v1'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v1Staging -Version '20260101-120000' -ConfigPath $configPath
        $v2Staging = Initialize-PromotionFixture -Root $installRoot -Version '20260102-120000' -ApiMarker 'v2'
        Invoke-TestReleasePromotion -InstallRoot $installRoot -StagingPath $v2Staging -Version '20260102-120000' -ConfigPath $configPath

        {
            Invoke-ReleaseRollbackFromState `
                -InstallRoot $installRoot `
                -TaskName 'UqebApi' `
                -ApiPort 5000 `
                -ConfigPath $configPath
        } | Should -Throw '*did not restart*'
    }

    It 'marks file rollback successful only after restart and health verification' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Invoke-ReleaseRollbackFromState { return $false }
        Mock Invoke-DeploymentFileRollback { return $true }
        Mock Sync-PublishCompatibilityLinks {}
        Mock Invoke-RestartCurrentReleaseService {}

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "new release health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match 'تم استرجاع ملفات API/Web/Chromium والتحقق من صحة الإصدار'
        Assert-MockCalled Invoke-DeploymentFileRollback -Times 1 -Exactly -ParameterFilter {
            $ConfigSource -eq $env.ConfigPath
        }
        Assert-MockCalled Invoke-RestartCurrentReleaseService -Times 1 -Exactly -ParameterFilter {
            $RequireHealthVerification
        }
    }

    It 'does not mark file rollback successful when health verification fails' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Invoke-ReleaseRollbackFromState { return $false }
        Mock Invoke-DeploymentFileRollback { return $true }
        Mock Sync-PublishCompatibilityLinks {}
        Mock Invoke-RestartCurrentReleaseService { throw 'rollback health failed' }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "new release health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match 'فشل التحقق بعد file rollback'
        $result.Text | Should -Not -Match 'تم استرجاع ملفات API/Web/Chromium والتحقق من صحة الإصدار'
        $result.Text | Should -Not -Match 'تم تنفيذ rollback للملفات فقط'
    }

    It 'prints rollback consistency fields when migration was applied before file rollback' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Invoke-ReleaseRollbackFromState { return $false }
        Mock Invoke-DeploymentFileRollback { return $true }
        Mock Sync-PublishCompatibilityLinks {}
        Mock Invoke-RestartCurrentReleaseService {}
        Mock Get-LatestAppliedMigrationId {
            return '20260622062754_AddReferenceDataNormalizedNames'
        }
        Mock Test-RequiredMigrationPresent { return $false }
        Mock Test-RequiredMigrationApplied {}

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath -PackageMigrationContent '$global:rollbackReportMigrationApplied = $true'
        'throw "new release health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match 'Rollback الملفات:'
        $result.Text | Should -Match 'Rollback قاعدة البيانات:'
        $result.Text | Should -Match 'هل تم تطبيق migrations أثناء النشر: نعم'
        $result.Text | Should -Match 'آخر migration قبل النشر:'
        $result.Text | Should -Match 'آخر migration بعد محاولة النشر:'
        $result.Text | Should -Match 'آخر migration بعد rollback/الفشل:'
        $result.Text | Should -Match 'احتمالية app/DB mismatch: نعم'
        $result.Text | Should -Match 'تم تطبيق migration أثناء النشر ولم يتم تنفيذ rollback تلقائي لقاعدة البيانات'
    }

    It 'warns about app DB mismatch when migration was applied and deployment fails before rollbackPerformed is set' {
        Register-StandardDeploymentInstallMocks -IncludeHealthMocks
        Mock Test-RequiredMigrationPresent { return $false }
        Mock Test-RequiredMigrationApplied {}
        Mock Install-StagedReleaseToProduction { throw 'promotion failed' }
        Mock Invoke-RestartCurrentReleaseService {}
        Mock Get-LatestAppliedMigrationId {
            return '20260628190617_AddDepartmentResponseWorkflow'
        }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath -PackageMigrationContent 'exit 0'

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match 'promotion failed'
        $result.Text | Should -Match 'هل تم تطبيق migrations أثناء النشر: نعم'
        $result.Text | Should -Match 'احتمالية app/DB mismatch: نعم'
        $result.Text | Should -Match 'تم تطبيق migration أثناء النشر ولم يتم تنفيذ rollback تلقائي لقاعدة البيانات'
        $result.Text | Should -Not -Match 'تم تنفيذ rollback للملفات فقط'
    }

    It 'does not warn about migration mismatch when no migration was applied and deployment fails' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Invoke-ReleaseRollbackFromState { return $false }
        Mock Invoke-DeploymentFileRollback { return $true }
        Mock Sync-PublishCompatibilityLinks {}
        Mock Invoke-RestartCurrentReleaseService {}

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'throw "new release health failed"' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Text | Should -Match 'هل تم تطبيق migrations أثناء النشر: لا'
        $result.Text | Should -Match 'احتمالية app/DB mismatch: لا'
        $result.Text | Should -Not -Match 'تم تطبيق migration أثناء النشر ولم يتم تنفيذ rollback تلقائي لقاعدة البيانات'
    }

    It 'prints unknown migration fields when migration report reads fail' {
        Register-StandardDeploymentInstallMocks -IncludePromotionMock -IncludeHealthMocks
        Mock Get-LatestAppliedMigrationId { throw 'SQL history unavailable' }

        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        $result = Get-InstallScriptOutput {
            Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env
        }

        $result.Threw | Should -BeFalse
        $result.Text | Should -Match 'آخر migration قبل النشر: غير معروف'
        $result.Text | Should -Match 'آخر migration بعد محاولة النشر: غير معروف'
        $result.Text | Should -Match 'آخر migration بعد rollback/الفشل: غير معروف'
    }

    It 'tracks service stop phases and restarts current release when stop started before promotion' {
        $content = Get-Content $script:InstallScript -Raw
        $content | Should -Match '\$scheduledTaskStopped\s*=\s*\$true'
        $content | Should -Match '\$listenersStopped\s*=\s*\$true'
        $content | Should -Match '\$portReleased\s*=\s*\$true'
        $content | Should -Match 'Wait-PortReleased'
        $content | Should -Match 'elseif \(\$scheduledTaskStopped\)'
        $content | Should -Match 'Invoke-RestartCurrentReleaseService'
    }
}

Describe 'build-production-package.ps1 policy' {
    It 'injects production frontend API base URL during build' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match 'VITE_API_BASE_URL'
        $content | Should -Match '10\.0\.177\.17:5000/api'
    }

    It 'does not use invalid dotnet test no-restore syntax' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Not -Match '--no-restore:\$false'
        $content | Should -Match 'dotnet test \$backendTests -c Release'
    }

    It 'runs npm ci and npm test in separate external command steps' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match '(?s)Invoke-ExternalCommand[^\{]+\{[^\}]*npm ci'
        $content | Should -Match '(?s)Invoke-ExternalCommand[^\{]+\{[^\}]*npm test'
        $content | Should -Not -Match 'npm ci\s+npm test'
    }

    It 'validates frontend dist API URL after production build' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match 'function Assert-FrontendDistApiBaseUrl'
        $content | Should -Match 'Assert-FrontendDistApiBaseUrl'
        $content | Should -Match 'ProductionApiBaseUrl'
        $content | Should -Match 'Get-ChildItem -LiteralPath \$DistPath -Recurse -File'
    }

    It 'rejects local API URLs from frontend dist' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match 'localhost:5000'
        $content | Should -Match '127\.0\.0\.1:5000'
        $content | Should -Match 'http://localhost'
        $content | Should -Match 'http://127\.0\.0\.1'
    }

    It 'requires build metadata in production package structure checks' {
        $repoRoot = Split-Path $PSScriptRoot -Parent
        $workflowPath = Join-Path (Join-Path (Join-Path $repoRoot '.github') 'workflows') 'deployment-package.yml'
        $workflow = Get-Content -LiteralPath $workflowPath -Raw
        $workflow | Should -Match 'api\\build-info\.json'
    }

    It 'uses UTC for package version stamp' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match '\[DateTime\]::UtcNow\.ToString\("yyyyMMdd-HHmmss"\)'
    }

    It 'does not rely on LASTEXITCODE after in-process install scripts' {
        $content = Get-Content (Join-Path $PSScriptRoot 'install-production-package.ps1') -Raw
        $content | Should -Not -Match '\$LASTEXITCODE'
    }

    It 'declares atomic release promotion contract in package manifest' {
        $buildContent = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $installContent = Get-Content (Join-Path $PSScriptRoot 'install-production-package.ps1') -Raw
        $buildContent | Should -Match 'promotionModel = "releases-current-v1"'
        $buildContent | Should -Match 'packageContractVersion = 2'
        $installContent | Should -Match 'Install-StagedReleaseToProduction'
        $installContent | Should -Match 'Test-RequiredMigrationPresent'
        $installContent | Should -Match 'deprecated and no longer required'
    }
}
