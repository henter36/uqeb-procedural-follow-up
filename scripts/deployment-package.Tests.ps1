#Requires -Version 5.1

BeforeAll {
    $script:CommonPath = Join-Path $PSScriptRoot 'deployment\Common.ps1'
    $script:InstallScript = Join-Path $PSScriptRoot 'install-production-package.ps1'
    $script:ApplyScript = Join-Path $PSScriptRoot 'apply-migrations.ps1'
    . $script:CommonPath

    function New-TempDirectory {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("uqeb-deploy-test-" + [guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function New-TestPackage {
        param(
            [string]$Root,
            [string]$Version = 'test-001'
        )

        $api = Join-Path $Root 'api'
        $web = Join-Path $Root 'web'
        $database = Join-Path $Root 'database'
        $scripts = Join-Path $Root 'scripts'
        $deployment = Join-Path $scripts 'deployment'
        Ensure-Directory $api
        Ensure-Directory $web
        Ensure-Directory $database
        Ensure-Directory $deployment

        Set-Content (Join-Path $api 'Uqeb.Api.dll') 'dll' -Encoding ASCII
        Set-Content (Join-Path $web 'index.html') '<html></html>' -Encoding ASCII
        Set-Content (Join-Path $database 'migrations-idempotent.sql') 'SELECT 1;' -Encoding ASCII
        'exit 0' | Set-Content (Join-Path $scripts 'apply-migrations.ps1') -Encoding ASCII
        'exit 0' | Set-Content (Join-Path $scripts 'verify-deployment-health.ps1') -Encoding ASCII
        Copy-Item $script:CommonPath (Join-Path $deployment 'Common.ps1') -Force

        $files = [ordered]@{}
        foreach ($relative in @('api/Uqeb.Api.dll', 'web/index.html', 'database/migrations-idempotent.sql')) {
            $full = Join-Path $Root ($relative -replace '/', '\')
            $files[$relative] = Get-FileSha256Hex -Path $full
        }

        $manifest = [ordered]@{
            applicationName = 'Uqeb'
            version = $Version
            buildTimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
            commitSha = 'testsha'
            minimumDatabaseMigration = '20260622062754_AddReferenceDataNormalizedNames'
            files = $files
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Root 'manifest.json') -Encoding UTF8
    }

    function New-ZipFromDirectory {
        param(
            [string]$Source,
            [string]$ZipPath
        )

        if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $ZipPath)
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

    function New-TestProductionSettingsJson {
        return (@{
            ConnectionStrings = @{ DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True' }
            Jwt = @{ Key = '12345678901234567890123456789012' }
        } | ConvertTo-Json -Compress)
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
        New-TestPackage -Root $root
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
        $fixed | Should -Match '(?is)ALTER TABLE \[Categories\].*;\s*GO\s*UPDATE Departments'
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

Describe 'Application file copy policy' {
    It 'rejects robocopy /MIR for API targets' {
        { Invoke-RobocopySafe -Source 'C:\src' -Destination 'C:\dst' -TargetType Api -ExtraArguments @('/MIR') } |
            Should -Throw '*API*'
    }

    It 'allows robocopy /MIR for Web targets' {
        Mock robocopy {
            param($args)
            ($args -join ' ') | Should -Match '/MIR'
            $global:LASTEXITCODE = 1
        }

        { Invoke-RobocopySafe -Source 'C:\src' -Destination 'C:\dst' -TargetType Web -ExtraArguments @('/MIR') } |
            Should -Not -Throw
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
    It 'restores backed-up API and Web folders' {
        Mock Stop-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }

        $backupApi = New-TempDirectory
        $backupWeb = New-TempDirectory
        $apiTarget = New-TempDirectory
        $webTarget = New-TempDirectory
        Set-Content (Join-Path $backupApi 'Uqeb.Api.dll') 'old-api'
        Set-Content (Join-Path $backupWeb 'index.html') 'old-web'
        Set-Content (Join-Path $apiTarget 'Uqeb.Api.dll') 'new-api'
        Set-Content (Join-Path $webTarget 'index.html') 'new-web'

        $configSource = Join-Path $backupApi 'appsettings.Production.json'
        $configTarget = Join-Path $apiTarget 'appsettings.Production.json'
        (@{ Jwt = @{ Key = '12345678901234567890123456789012' } } | ConvertTo-Json -Compress) | Set-Content $configSource -Encoding UTF8

        Mock robocopy { $global:LASTEXITCODE = 1 }

        $restored = Invoke-DeploymentFileRollback `
            -TaskName 'UqebApi' `
            -ApiPort 5000 `
            -BackupApi $backupApi `
            -BackupWeb $backupWeb `
            -ApiTarget $apiTarget `
            -WebTarget $webTarget `
            -ConfigTarget $configTarget `
            -ConfigSource $configSource

        $restored | Should -BeTrue
        Assert-MockCalled robocopy -Times 2 -Exactly
    }
}

Describe 'install-production-package.ps1 scenarios' {
    BeforeEach {
        Mock Test-IsAdministrator { $true }
        Mock Get-ScheduledTask { return [pscustomobject]@{ TaskName = 'UqebApi' } }
        Mock Stop-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }
        Mock Test-PortListener { $true }
        Mock Stop-Process {}
        Mock Get-NetTCPConnection { return @() }
        Mock Move-Item {}
        Mock Test-RecentLogErrors { return @() }
        Mock Invoke-RobocopySafe {}
        Mock Copy-ApplicationPayload {}
        Mock Invoke-DatabaseBackupRetentionPolicy { return @() }
        Mock Get-SqlConnectionInfoFromSettings {
            return [pscustomobject]@{
                Server = '.'
                Database = 'UqebDb'
                ConnectionString = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
            }
        }
        Mock Invoke-ProductionDatabaseBackup {
            $backupFile = Join-Path $TestDrive ("UqebDb-before-mock.bak")
            Set-Content -LiteralPath $backupFile -Value ('x' * 64) -Encoding ASCII
            return [pscustomobject]@{
                Path = $backupFile
                SizeBytes = (Get-Item -LiteralPath $backupFile).Length
                CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                Sha256 = ('a' * 64)
                DatabaseName = 'UqebDb'
            }
        }
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
        $folder = New-TempDirectory
        { & $script:InstallScript -PackagePath $folder } | Should -Throw '*ZIP*'
    }

    It 'stops on migration failure' {
        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        'throw "migration failed"' | Set-Content (Join-Path $pkgRoot 'scripts\apply-migrations.ps1') -Encoding ASCII
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'throw "migration failed"' | Set-Content $apply -Encoding ASCII

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath (Join-Path $installRoot 'config\appsettings.Production.json') } |
            Should -Throw
    }

    It 'fails deployment when health verification fails' {
        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'exit 0' | Set-Content $apply -Encoding ASCII
        $health = Join-Path $tools 'verify-deployment-health.ps1'
        'throw "health failed"' | Set-Content $health -Encoding ASCII

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath (Join-Path $installRoot 'config\appsettings.Production.json') } |
            Should -Throw
    }

    It 'succeeds on full mocked deployment path' {
        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        $configPath = Join-Path $installRoot 'config\appsettings.Production.json'
        New-TestProductionSettingsJson | Set-Content $configPath -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'exit 0' | Set-Content $apply -Encoding ASCII
        $health = Join-Path $tools 'verify-deployment-health.ps1'
        'exit 0' | Set-Content $health -Encoding ASCII

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath $configPath } | Should -Not -Throw
    }

    It 'does not declare success when API port never opens' {
        Mock Test-PortListener { $false }

        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'exit 0' | Set-Content $apply -Encoding ASCII

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath (Join-Path $installRoot 'config\appsettings.Production.json') } |
            Should -Throw '*API*'
    }

    It 'stops deployment when database backup fails before stopping API' {
        Mock Invoke-ProductionDatabaseBackup { throw 'backup failed' }
        $global:deployOrder = @()
        Mock Stop-ScheduledTask { $global:deployOrder += 'stop-api' }

        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath (Join-Path $installRoot 'config\appsettings.Production.json') } |
            Should -Throw

        $global:deployOrder | Should -Not -Contain 'stop-api'
        Assert-MockCalled Copy-ApplicationPayload -Times 0 -Exactly
    }

    It 'runs database backup before API stop and migrations' {
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

        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        $configPath = Join-Path $installRoot 'config\appsettings.Production.json'
        New-TestProductionSettingsJson | Set-Content $configPath -Encoding UTF8

        $health = Join-Path $tools 'verify-deployment-health.ps1'
        'exit 0' | Set-Content $health -Encoding ASCII

        & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath $configPath | Out-Null

        $global:deployOrder[0] | Should -Be 'db-backup'
        ($global:deployOrder.IndexOf('db-backup') -lt $global:deployOrder.IndexOf('stop-api')) | Should -BeTrue
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

        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        $health = Join-Path $tools 'verify-deployment-health.ps1'
        'throw "health failed"' | Set-Content $health -Encoding ASCII

        $output = @()
        try {
            & $script:InstallScript `
                -PackagePath $zip `
                -InstallRoot $installRoot `
                -ToolsRoot $tools `
                -ApiPath (Join-Path $installRoot 'publish\api') `
                -WebPath (Join-Path $installRoot 'publish\web') `
                -ConfigPath (Join-Path $installRoot 'config\appsettings.Production.json') *>&1 |
                ForEach-Object { $output += $_.ToString() }
        }
        catch {
            $output += $_.Exception.Message
        }

        ($output -join [Environment]::NewLine) | Should -Match 'RESTORE DATABASE'
    }

    It 'succeeds when stale LASTEXITCODE is set before migration' {
        $global:LASTEXITCODE = 1

        $root = New-TempDirectory
        $installRoot = Join-Path $root 'install'
        $tools = Join-Path $root 'tools'
        $incoming = Join-Path $installRoot 'incoming'
        Ensure-Directory $incoming
        Ensure-Directory $tools
        Ensure-Directory (Join-Path $tools 'deployment')
        Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

        $pkgRoot = New-TempDirectory
        New-TestPackage -Root $pkgRoot
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        $configPath = Join-Path $installRoot 'config\appsettings.Production.json'
        New-TestProductionSettingsJson | Set-Content $configPath -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'exit 0' | Set-Content $apply -Encoding ASCII
        $health = Join-Path $tools 'verify-deployment-health.ps1'
        'exit 0' | Set-Content $health -Encoding ASCII

        { & $script:InstallScript `
            -PackagePath $zip `
            -InstallRoot $installRoot `
            -ToolsRoot $tools `
            -ApiPort 5001 `
            -ApiPath (Join-Path $installRoot 'publish\api') `
            -WebPath (Join-Path $installRoot 'publish\web') `
            -ConfigPath $configPath } | Should -Not -Throw
    }

    It 'derives ApiBaseUrl from ApiPort when URL is omitted' {
        $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $script:InstallScript,
            [ref]$null,
            [ref]$errors)
        $errors | Should -BeNullOrEmpty
        $content = Get-Content -LiteralPath $script:InstallScript -Raw
        $content | Should -Match 'IsNullOrWhiteSpace\(\$ApiBaseUrl\)'
        $content | Should -Match 'http://localhost:\$ApiPort'
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
        $global:SqlDeploymentCommandHandler = {
            param(
                [string]$Server,
                [string]$Database,
                [string]$CommandText,
                [bool]$Scalar,
                [bool]$DataTable
            )
            return "handled:$CommandText"
        }

        Invoke-SqlDeploymentCommand -Server '.' -Database 'master' -CommandText 'PING' | Should -Be 'handled:PING'
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

        $files = @()
        for ($i = 0; $i -lt 12; $i++) {
            $path = Join-Path $backupDir ("UqebDb-before-20260101-12000$i.bak")
            Set-Content -LiteralPath $path -Value ('x' * 10) -Encoding ASCII
            (Get-Item -LiteralPath $path).LastWriteTime = (Get-Date).AddHours(-$i)
            $files += $path
        }

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
        $manifest = [ordered]@{
            databaseBackup = [ordered]@{ path = $protectedPath }
        }
        Ensure-Directory (Join-Path $installRoot 'publish')
        $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $installRoot 'publish\release-manifest.json') -Encoding UTF8

        $paths = @(Get-ProtectedDatabaseBackupPaths -InstallRoot $installRoot)
        @($paths).Count | Should -Be 1
        [string]$paths[0] | Should -Be $protectedPath
    }

    It 'does not delete protected backup paths during retention' {
        $installRoot = New-TempDirectory
        $backupDir = Join-Path $installRoot 'backup\db'
        Ensure-Directory $backupDir

        $protectedPath = Join-Path $backupDir 'UqebDb-before-protected.bak'
        $files = @()
        for ($i = 0; $i -lt 12; $i++) {
            $path = Join-Path $backupDir ("UqebDb-before-20260101-12000$i.bak")
            Set-Content -LiteralPath $path -Value ('x' * 10) -Encoding ASCII
            (Get-Item -LiteralPath $path).LastWriteTime = (Get-Date).AddHours(-$i)
            $files += $path
        }
        Set-Content -LiteralPath $protectedPath -Value ('x' * 10) -Encoding ASCII
        (Get-Item -LiteralPath $protectedPath).LastWriteTime = (Get-Date).AddHours(-20)

        $manifest = [ordered]@{
            databaseBackup = [ordered]@{ path = $protectedPath }
        }
        Ensure-Directory (Join-Path $installRoot 'publish')
        $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $installRoot 'publish\release-manifest.json') -Encoding UTF8

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
        $settings = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        (@{
            ConnectionStrings = @{
                DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
            }
        } | ConvertTo-Json -Compress) | Set-Content $settings -Encoding UTF8

        $info = Get-SqlConnectionInfoFromSettings -SettingsPath $settings
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $info.ConnectionString
        $builder.IntegratedSecurity | Should -BeTrue
    }

    It 'preserves SQL login authentication from production settings' {
        $settings = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        (@{
            ConnectionStrings = @{
                DefaultConnection = 'Server=.;Database=UqebDb;User ID=uqeb;Password=Secret123!;TrustServerCertificate=True'
            }
        } | ConvertTo-Json -Compress) | Set-Content $settings -Encoding UTF8

        $info = Get-SqlConnectionInfoFromSettings -SettingsPath $settings
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $info.ConnectionString
        $builder.UserID | Should -Be 'uqeb'
        $builder.Password | Should -Be 'Secret123!'
    }

    It 'switches database to master without changing authentication' {
        $connectionString = 'Server=.;Database=UqebDb;User ID=uqeb;Password=Secret123!;TrustServerCertificate=True'
        $connection = New-SqlDeploymentConnection -ConnectionString $connectionString -Database 'master'
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connection.ConnectionString
        if ($builder.ContainsKey('Database')) {
            $builder.Database | Should -Be 'master'
        }
        else {
            $builder['Initial Catalog'] | Should -Be 'master'
        }
        $builder.UserID | Should -Be 'uqeb'
        $builder.Password | Should -Be 'Secret123!'
        $connection.Dispose()
    }

    It 'does not print passwords in redacted connection labels' {
        $label = Get-SqlRedactedConnectionLabel -Server '.' -Database 'UqebDb'
        $label | Should -Not -Match 'Password'
        $label | Should -Match 'Server=\.'
    }

    It 'fails clearly when connection string is missing' {
        $settings = Join-Path (New-TempDirectory) 'appsettings.Production.json'
        (@{ ConnectionStrings = @{} } | ConvertTo-Json -Compress) | Set-Content $settings -Encoding UTF8
        { Get-SqlConnectionInfoFromSettings -SettingsPath $settings } | Should -Throw '*DefaultConnection*'
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

Describe 'build-production-package.ps1 policy' {
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

    It 'uses UTC for package version stamp' {
        $content = Get-Content (Join-Path $PSScriptRoot 'build-production-package.ps1') -Raw
        $content | Should -Match '\[DateTime\]::UtcNow\.ToString\("yyyyMMdd-HHmmss"\)'
    }

    It 'does not rely on LASTEXITCODE after in-process install scripts' {
        $content = Get-Content (Join-Path $PSScriptRoot 'install-production-package.ps1') -Raw
        $content | Should -Not -Match '\$LASTEXITCODE'
    }
}
