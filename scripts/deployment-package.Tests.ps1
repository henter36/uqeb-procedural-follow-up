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

Describe 'Application file copy policy' {
    It 'does not use robocopy /MIR' {
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
        Mock Invoke-RobocopyWithoutMirror {}
        Mock Copy-ApplicationPayload {}
        Mock Get-SqlConnectionInfoFromSettings {
            return [pscustomobject]@{ Server = '.'; Database = 'UqebDb' }
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
        'exit 1' | Set-Content (Join-Path $pkgRoot 'scripts\apply-migrations.ps1') -Encoding ASCII
        $zip = Join-Path $incoming 'Uqeb-test.zip'
        New-ZipFromDirectory -Source $pkgRoot -ZipPath $zip
        $hash = Get-FileSha256Hex -Path $zip
        Set-Content (Join-Path $incoming 'Uqeb-test.sha256.txt') "$hash  Uqeb-test.zip" -Encoding ASCII
        Ensure-Directory (Join-Path $installRoot 'config')
        New-TestProductionSettingsJson | Set-Content (Join-Path $installRoot 'config\appsettings.Production.json') -Encoding UTF8

        $apply = Join-Path $tools 'apply-migrations.ps1'
        'exit 1' | Set-Content $apply -Encoding ASCII

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
}
