#Requires -Version 5.1

BeforeAll {
    $script:RepoScriptsRoot = Split-Path $PSScriptRoot -Parent
    . (Join-Path $PSScriptRoot 'DeploymentTestHelpers.ps1')
    $script:CommonPath = Join-Path $script:RepoScriptsRoot 'deployment\Common.ps1'
    $script:InstallScript = Join-Path $script:RepoScriptsRoot 'install-production-package.ps1'
    . $script:CommonPath
    $script:DeploymentTest = Initialize-DeploymentTestSession -ScriptsRoot $script:RepoScriptsRoot
}

Describe 'Windows installed-artifact promotion proof' {
    BeforeEach {
        $script:Root = New-DeploymentTestTempDirectory -Prefix 'uqeb-installed-proof-'
        $script:InstallRoot = Join-Path $Root 'install'
        $script:StagingPath = Join-Path $Root 'staging'
        $script:Version = 'proof-20260625-120000'
        Initialize-ApiWebPayload `
            -ApiPath (Join-Path $StagingPath 'api') `
            -WebPath (Join-Path $StagingPath 'web') `
            -Marker 'proof-staged'
        Mock-RobocopyAsCopyOnNonWindows
    }

    AfterEach {
        Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'materializes releases/current and rollback-state.json from staged payload' {
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $InstallRoot

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
        $configPath = New-AuthoritativeProductionConfig -InstallRoot $InstallRoot

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
        Register-StandardDeploymentInstallMocks
    }

    It 'writes rollback-state and release manifest during mocked Windows install' -Skip:(-not $IsWindows) {
        $env = New-InstallTestEnvironment -CommonPath $script:CommonPath -PackageVersion 'proof-install'
        'exit 0' | Set-Content (Join-Path $env.ToolsRoot 'verify-deployment-health.ps1') -Encoding ASCII

        Invoke-TestInstallScript -InstallScript $script:InstallScript -Environment $env | Out-Null

        Test-Path -LiteralPath (Get-RollbackStatePath -InstallRoot $env.InstallRoot) | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $env.InstallRoot 'releases\proof-install\api\Uqeb.Api.dll') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $env.InstallRoot 'publish\release-manifest.json') | Should -BeTrue
        (Get-Content (Join-Path $env.InstallRoot 'publish\release-manifest.json') -Raw | ConvertFrom-Json).promotionModel |
            Should -Be 'releases-current-v1'
    }
}
