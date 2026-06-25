#Requires -Version 5.1

BeforeAll {
    $script:RepoScriptsRoot = Split-Path $PSScriptRoot -Parent
    $script:CommonPath = Join-Path $script:RepoScriptsRoot 'deployment\Common.ps1'
    $script:InstallScript = Join-Path $script:RepoScriptsRoot 'install-production-package.ps1'
    . $script:CommonPath

    function script:New-ProofRoot {
        $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
        $path = Join-Path $base ("uqeb-installed-proof-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function script:Initialize-ProofPayload {
        param(
            [string]$ApiPath,
            [string]$WebPath,
            [string]$Marker
        )

        Ensure-Directory $ApiPath
        Ensure-Directory $WebPath
        Set-Content -LiteralPath (Join-Path $ApiPath 'Uqeb.Api.dll') -Value $Marker -Encoding ASCII
        Set-Content -LiteralPath (Join-Path $WebPath 'index.html') -Value "<html>$Marker</html>" -Encoding ASCII
    }

    function script:Mock-RobocopyAsCopy {
        if ($IsWindows) {
            return
        }

        Mock Invoke-RobocopySafe {
            param(
                [string]$Source,
                [string]$Destination
            )

            Ensure-Directory $Destination
            Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
            }
        }
    }
}

Describe 'Windows installed-artifact promotion proof' {
    BeforeEach {
        $script:Root = New-ProofRoot
        $script:InstallRoot = Join-Path $Root 'install'
        $script:StagingPath = Join-Path $Root 'staging'
        $script:Version = 'proof-20260625-120000'
        Initialize-ProofPayload `
            -ApiPath (Join-Path $StagingPath 'api') `
            -WebPath (Join-Path $StagingPath 'web') `
            -Marker 'proof-staged'
        Mock-RobocopyAsCopy
    }

    AfterEach {
        Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'materializes releases/current and rollback-state.json from staged payload' {
        $configPath = Join-Path $InstallRoot 'config\appsettings.Production.json'
        Ensure-Directory (Split-Path -Parent $configPath)
        (@{ ConnectionStrings = @{ DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True' } } |
            ConvertTo-Json -Compress) | Set-Content -LiteralPath $configPath -Encoding UTF8

        $promotion = Install-StagedReleaseToProduction `
            -StagingPath $StagingPath `
            -InstallRoot $InstallRoot `
            -Version $Version `
            -ConfigPath $configPath `
            -PackagePath 'Uqeb-proof.zip' `
            -PackageCommit 'proofsha'

        $releaseApi = Join-Path $InstallRoot (Join-Path (Join-Path 'releases' $Version) (Join-Path 'api' 'Uqeb.Api.dll'))
        $currentApi = Join-Path $InstallRoot (Join-Path 'current' (Join-Path 'api' 'Uqeb.Api.dll'))
        $rollbackState = Get-RollbackStatePath -InstallRoot $InstallRoot

        Test-Path -LiteralPath $releaseApi | Should -BeTrue
        Test-Path -LiteralPath $currentApi | Should -BeTrue
        Test-Path -LiteralPath $rollbackState | Should -BeTrue
        (Get-Content -LiteralPath $currentApi -Raw).Trim() | Should -Be 'proof-staged'

        $state = Read-RollbackState -InstallRoot $InstallRoot
        $state.currentRelease | Should -Be $Version
        $promotion.ConfigTarget | Should -Be (Join-Path $InstallRoot 'current\api\appsettings.Production.json')
    }

    It 'creates publish junctions to current on Windows' -Skip:(-not $IsWindows) {
        $configPath = Join-Path $InstallRoot 'config\appsettings.Production.json'
        Ensure-Directory (Split-Path -Parent $configPath)
        (@{ ConnectionStrings = @{ DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True' } } |
            ConvertTo-Json -Compress) | Set-Content -LiteralPath $configPath -Encoding UTF8

        Install-StagedReleaseToProduction `
            -StagingPath $StagingPath `
            -InstallRoot $InstallRoot `
            -Version $Version `
            -ConfigPath $configPath `
            -PackagePath 'Uqeb-proof.zip' `
            -PackageCommit 'proofsha' | Out-Null

        Sync-PublishCompatibilityLinks -InstallRoot $InstallRoot

        $publishApi = Join-Path $InstallRoot 'publish\api'
        $currentApi = Join-Path $InstallRoot 'current\api'
        (Get-Item -LiteralPath $publishApi).Attributes | Should -Match 'ReparsePoint'
        (Get-Content -LiteralPath (Join-Path $publishApi 'Uqeb.Api.dll') -Raw).Trim() | Should -Be 'proof-staged'
        (Resolve-Path -LiteralPath $publishApi).Path | Should -Not -Be (Resolve-Path -LiteralPath $currentApi).Path
    }
}

Describe 'Windows offline installer artifact proof' {
    BeforeEach {
        Mock Test-IsAdministrator { $true }
        Mock Get-ScheduledTask { return [pscustomobject]@{ TaskName = 'UqebApi' } }
        Mock Stop-ScheduledTask {}
        Mock Start-ScheduledTask {}
        Mock Stop-ApiListenersOnPort {}
        Mock Wait-PortReleased { $true }
        Mock Test-PortListener { $true }
        Mock Test-RecentLogErrors { return @() }
        Mock Invoke-ProductionDatabaseBackup {
            return [pscustomobject]@{
                Path = 'C:\Uqeb\backup\db\UqebDb-before-proof.bak'
                SizeBytes = 1024
                CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
                Sha256 = ('a' * 64)
            }
        }
        Mock Invoke-DatabaseBackupRetentionPolicy { return @() }
        Mock Install-PlaywrightBrowserToProduction { return '' }
        Mock Test-PlaywrightBrowserPayload {
            param([string]$BrowsersRoot)

            $executable = Join-Path $BrowsersRoot 'chromium-proof\chrome.exe'
            Ensure-Directory (Split-Path -Parent $executable)
            Set-Content -LiteralPath $executable -Value 'fake-chromium' -Encoding ASCII
            return [pscustomobject]@{ FullPath = $executable }
        }
        Mock Test-PlaywrightPackagePreflight {
            return [pscustomobject]@{
                ExecutableRelativePath = 'chromium-proof\chrome.exe'
            }
        }
        Mock-RobocopyAsCopy
    }

    It 'writes rollback-state and release manifest during mocked Windows install' -Skip:(-not $IsWindows) {
        $root = New-ProofRoot
        try {
            $installRoot = Join-Path $root 'install'
            $tools = Join-Path $root 'tools'
            $incoming = Join-Path $installRoot 'incoming'
            Ensure-Directory $incoming
            Ensure-Directory (Join-Path $tools 'deployment')
            Copy-Item $script:CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

            $pkgRoot = Join-Path $root 'package'
            Ensure-Directory (Join-Path $pkgRoot 'api')
            Ensure-Directory (Join-Path $pkgRoot 'web')
            Ensure-Directory (Join-Path $pkgRoot 'database')
            Ensure-Directory (Join-Path $pkgRoot 'browsers')
            Ensure-Directory (Join-Path $pkgRoot 'scripts')
            Set-Content (Join-Path $pkgRoot 'api\Uqeb.Api.dll') 'dll' -Encoding ASCII
            Set-Content (Join-Path $pkgRoot 'api\playwright.ps1') 'exit 0' -Encoding ASCII
            Set-Content (Join-Path $pkgRoot 'web\index.html') '<html></html>' -Encoding ASCII
            Set-Content (Join-Path $pkgRoot 'database\migrations-idempotent.sql') 'SELECT 1;' -Encoding ASCII
            Set-Content (Join-Path $pkgRoot 'browsers\playwright-browser-manifest.json') '{}' -Encoding ASCII
            'exit 0' | Set-Content (Join-Path $pkgRoot 'scripts\apply-migrations.ps1') -Encoding ASCII

            $browserRoot = Join-Path $installRoot 'tools\ms-playwright'
            Ensure-Directory $browserRoot
            $browserExecutable = Join-Path $browserRoot 'chromium-proof\chrome.exe'
            Ensure-Directory (Split-Path -Parent $browserExecutable)
            Set-Content -LiteralPath $browserExecutable -Value 'fake-chromium' -Encoding ASCII
            $browserHash = Get-FileSha256Hex -Path $browserExecutable

            $manifest = [ordered]@{
                version = 'proof-install'
                commitSha = 'proofsha'
                minimumDatabaseMigration = '20260622062754_AddReferenceDataNormalizedNames'
                playwright = [ordered]@{
                    browserExecutableSha256 = $browserHash
                }
                files = [ordered]@{}
            }
            $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $pkgRoot 'manifest.json') -Encoding UTF8

            $zip = Join-Path $incoming 'Uqeb-proof-install.zip'
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::CreateFromDirectory($pkgRoot, $zip)
            $hash = Get-FileSha256Hex -Path $zip
            Set-Content (Join-Path $incoming 'Uqeb-proof-install.sha256.txt') "$hash  Uqeb-proof-install.zip" -Encoding ASCII

            $configPath = Join-Path $installRoot 'config\appsettings.Production.json'
            Ensure-Directory (Split-Path -Parent $configPath)
            (@{
                ConnectionStrings = @{ DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True' }
                Jwt = @{ Key = '12345678901234567890123456789012' }
            } | ConvertTo-Json -Compress) | Set-Content -LiteralPath $configPath -Encoding UTF8

            'exit 0' | Set-Content (Join-Path $tools 'verify-deployment-health.ps1') -Encoding ASCII

            Mock Get-SqlConnectionInfoFromSettings {
                return [pscustomobject]@{
                    Server = '.'
                    Database = 'UqebDb'
                    ConnectionString = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
                }
            }
            Mock Test-PackageManifestHashes {}

            & $script:InstallScript `
                -PackagePath $zip `
                -InstallRoot $installRoot `
                -ToolsRoot $tools `
                -ApiPath (Join-Path $installRoot 'publish\api') `
                -WebPath (Join-Path $installRoot 'publish\web') `
                -ConfigPath $configPath `
                -PlaywrightBrowsersPath (Join-Path $installRoot 'tools\ms-playwright')

            Test-Path -LiteralPath (Get-RollbackStatePath -InstallRoot $installRoot) | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $installRoot 'releases\proof-install\api\Uqeb.Api.dll') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $installRoot 'publish\release-manifest.json') | Should -BeTrue
            (Get-Content (Join-Path $installRoot 'publish\release-manifest.json') -Raw | ConvertFrom-Json).promotionModel |
                Should -Be 'releases-current-v1'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
