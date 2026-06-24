BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-PlaywrightTestDirectory {
        $path = Join-Path $env:TEMP ("uqeb-deploy-test-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function script:New-FakeChromiumTree {
        param([string]$Root)

        $executableDir = Join-Path $Root 'chromium-1\chrome-win64'
        New-Item -ItemType Directory -Path $executableDir -Force | Out-Null
        $executable = Join-Path $executableDir 'chrome.exe'
        Set-Content -LiteralPath $executable -Value 'fake-chromium' -Encoding ASCII
        return $executable
    }
}

Describe 'Playwright deployment helpers' {
    It 'rejects path traversal in relative package paths' {
        { Test-SafeRelativePackagePath -RelativePath '..\outside\chrome.exe' } |
            Should -Throw
    }

    It 'finds chromium executable in browser tree' {
        $root = New-PlaywrightTestDirectory
        try {
            $executable = New-FakeChromiumTree -Root $root
            $found = Get-PlaywrightBrowserExecutable -BrowsersRoot $root
            $found.FullPath | Should -Be $executable
            $found.RelativePath | Should -Be 'chromium-1/chrome-win64/chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'fails when chromium executable is missing' {
        $root = New-PlaywrightTestDirectory
        try {
            { Get-PlaywrightBrowserExecutable -BrowsersRoot $root } |
                Should -Throw
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects skip migration when required migration is missing' {
        $global:SqlDeploymentCommandHandler = {
            param($Server, $Database, $CommandText, $Scalar, $DataTable, $ConnectionString)
            if ($DataTable) {
                $table = New-Object System.Data.DataTable
                [void]$table.Columns.Add('MigrationId')
                return $table
            }
            return $null
        }

        try {
            { Test-RequiredMigrationApplied -ConnectionString 'Server=.;Database=Uqeb;Trusted_Connection=True;' -RequiredMigrationId '20260624064014_AlignReportingModelSnapshotAfterPreviewFix' } |
                Should -Throw
        }
        finally {
            Remove-Item Variable:global:SqlDeploymentCommandHandler -ErrorAction SilentlyContinue
        }
    }

    It 'allows skip migration when required migration exists' {
        $global:SqlDeploymentCommandHandler = {
            param($Server, $Database, $CommandText, $Scalar, $DataTable, $ConnectionString)
            if ($DataTable) {
                return ,@('20260624064014_AlignReportingModelSnapshotAfterPreviewFix')
            }
            return $null
        }

        try {
            $null -ne $global:SqlDeploymentCommandHandler | Should -BeTrue
            $appliedIds = Get-AppliedMigrationIds -ConnectionString 'Server=.;Database=Uqeb;Trusted_Connection=True;'
            $appliedIds | Should -Contain '20260624064014_AlignReportingModelSnapshotAfterPreviewFix'

            { Test-RequiredMigrationApplied -ConnectionString 'Server=.;Database=Uqeb;Trusted_Connection=True;' -RequiredMigrationId '20260624064014_AlignReportingModelSnapshotAfterPreviewFix' } |
                Should -Not -Throw
        }
        finally {
            Remove-Item Variable:global:SqlDeploymentCommandHandler -ErrorAction SilentlyContinue
        }
    }

    It 'writes PLAYWRIGHT_BROWSERS_PATH into run-api script' {
        $root = New-PlaywrightTestDirectory
        try {
            $runScript = Join-Path $root 'run-api.cmd'
            Write-ApiRunScript `
                -RunScriptPath $runScript `
                -ApiPath 'C:\Uqeb\publish\api' `
                -ApiPort 5000 `
                -PlaywrightBrowsersPath 'C:\Uqeb\tools\ms-playwright' `
                -LogPath 'C:\Uqeb\logs\api-runtime.log'

            $content = Get-Content -LiteralPath $runScript -Raw
            $content | Should -Match 'PLAYWRIGHT_BROWSERS_PATH=C:\\Uqeb\\tools\\ms-playwright'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'performs atomic browser directory swap' {
        $root = New-PlaywrightTestDirectory
        try {
            $target = Join-Path $root 'ms-playwright'
            $source = Join-Path $root 'package-browsers'
            New-FakeChromiumTree -Root $source | Out-Null

            $previous = Copy-DirectoryAtomically -Source $source -Target $target
            Test-Path -LiteralPath (Join-Path $target 'chromium-1\chrome-win64\chrome.exe') | Should -BeTrue
            $previous | Should -BeNullOrEmpty
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'build-production-package.ps1 playwright policy' {
    It 'includes chromium download via published playwright.ps1 by default' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match 'Invoke-PlaywrightChromiumInstall'
        $content | Should -Match 'playwright\.ps1'
        $content | Should -Match 'SkipPlaywrightBrowserDownload'
        $content | Should -Not -Match 'npx playwright install'
    }

    It 'includes browsers folder in package manifest' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match 'browsers\\playwright-browser-manifest\.json'
        $content | Should -Match 'playwright'
    }
}

Describe 'install-production-package.ps1 playwright policy' {
    It 'validates playwright payload before database backup' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $match = [regex]::Match($content, 'Test-PlaywrightPackagePreflight[\s\S]*Invoke-ProductionDatabaseBackup')
        $match.Success | Should -BeTrue
    }

    It 'guards skip migration with required migration check' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $content | Should -Match 'Test-RequiredMigrationApplied'
    }

    It 'sets PLAYWRIGHT_BROWSERS_PATH via run-api wrapper' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $content | Should -Match 'Write-ApiRunScript'
        $content | Should -Match 'PlaywrightBrowsersPath'
    }
}
