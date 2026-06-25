BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-PlaywrightTestDirectory {
        $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
        $path = Join-Path $base ("uqeb-deploy-test-" + [Guid]::NewGuid().ToString('N'))
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

    function script:New-PlaywrightPackageFixture {
        $root = New-PlaywrightTestDirectory
        $apiDir = Join-Path $root 'api'
        $browsersDir = Join-Path $root 'browsers'
        New-Item -ItemType Directory -Path $apiDir -Force | Out-Null
        New-Item -ItemType Directory -Path $browsersDir -Force | Out-Null

        $playwrightScript = Join-Path $apiDir 'playwright.ps1'
        Set-Content -LiteralPath $playwrightScript -Value 'playwright-script' -Encoding ASCII -NoNewline

        $executableRelativePath = 'chromium-1\chrome-win64\chrome.exe'
        $executableDir = Join-Path $browsersDir 'chromium-1\chrome-win64'
        New-Item -ItemType Directory -Path $executableDir -Force | Out-Null
        $executable = Join-Path $executableDir 'chrome.exe'
        Set-Content -LiteralPath $executable -Value 'fake-chromium' -Encoding ASCII -NoNewline

        $browserManifest = [ordered]@{
            executableRelativePath = $executableRelativePath
        }
        ($browserManifest | ConvertTo-Json -Depth 3) |
            Set-Content -LiteralPath (Join-Path $browsersDir 'playwright-browser-manifest.json') -Encoding UTF8

        return [pscustomobject]@{
            Root = $root
            PlaywrightScript = $playwrightScript
            Executable = $executable
            ScriptHash = (Get-FileSha256Hex -Path $playwrightScript)
            BrowserHash = (Get-FileSha256Hex -Path $executable)
        }
    }

    function script:New-PlaywrightPackageManifest {
        param(
            [bool]$Required = $true,
            [string]$PlaywrightScriptSha256 = '',
            [string]$BrowserExecutableSha256 = ''
        )

        return [pscustomobject]@{
            playwright = [pscustomobject]@{
                required = $Required
                playwrightScriptSha256 = $PlaywrightScriptSha256
                browserExecutableSha256 = $BrowserExecutableSha256
            }
        }
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
            ($found.RelativePath -replace '\\', '/') | Should -Be 'chromium-1/chrome-win64/chrome.exe'
            Test-Path -LiteralPath $found.FullPath | Should -BeTrue
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

    It 'finds a single chromium executable candidate' {
        $root = New-PlaywrightTestDirectory
        try {
            New-FakeChromiumTree -Root $root | Out-Null
            $found = Get-PlaywrightBrowserExecutable -BrowsersRoot $root
            $found.RelativePath | Should -Be 'chromium-1/chrome-win64/chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'throws when no chromium executable candidates exist' {
        $root = New-PlaywrightTestDirectory
        try {
            { Get-PlaywrightBrowserExecutable -BrowsersRoot $root } | Should -Throw
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'throws when multiple chromium executable candidates exist' {
        $root = New-PlaywrightTestDirectory
        try {
            New-FakeChromiumTree -Root $root | Out-Null
            $secondDir = Join-Path $root 'chromium-2\chrome-win64'
            New-Item -ItemType Directory -Path $secondDir -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $secondDir 'chrome.exe') -Value 'second' -Encoding ASCII

            { Get-PlaywrightBrowserExecutable -BrowsersRoot $root } |
                Should -Throw '*أكثر من Chromium executable*'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Invoke-PlaywrightChromiumInstall environment' {
    function script:New-FakePlaywrightInstallScript {
        param(
            [string]$Path,
            [int]$ExitCode = 0
        )

        @"
param([Parameter(ValueFromRemainingArguments = `$true)][string[]]`$args)
if (`$args -contains 'install') {
    `$root = `$env:PLAYWRIGHT_BROWSERS_PATH
    `$dir = Join-Path `$root 'chromium-1\chrome-win64'
    New-Item -ItemType Directory -Path `$dir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path `$dir 'chrome.exe') -Value 'fake' -Encoding ASCII
    exit $ExitCode
}
exit 1
"@ | Set-Content -LiteralPath $Path -Encoding ASCII
    }

    function script:New-InstallTestRoot {
        $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
        $path = Join-Path $base ("uqeb-playwright-install-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    It 'removes PLAYWRIGHT_BROWSERS_PATH when it was unset' {
        $root = New-InstallTestRoot
        $browsersRoot = Join-Path $root 'browsers'
        $scriptPath = Join-Path $root 'playwright.ps1'
        try {
            Remove-Item -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' -ErrorAction SilentlyContinue
            New-FakePlaywrightInstallScript -Path $scriptPath

            $null = Invoke-PlaywrightChromiumInstall -PlaywrightScriptPath $scriptPath -BrowsersRoot $browsersRoot

            Test-Path -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' | Should -BeFalse
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' -ErrorAction SilentlyContinue
        }
    }

    It 'restores PLAYWRIGHT_BROWSERS_PATH when it was previously set' {
        $root = New-InstallTestRoot
        $browsersRoot = Join-Path $root 'browsers'
        $scriptPath = Join-Path $root 'playwright.ps1'
        try {
            $env:PLAYWRIGHT_BROWSERS_PATH = 'C:\existing-cache'
            New-FakePlaywrightInstallScript -Path $scriptPath

            $null = Invoke-PlaywrightChromiumInstall -PlaywrightScriptPath $scriptPath -BrowsersRoot $browsersRoot

            $env:PLAYWRIGHT_BROWSERS_PATH | Should -Be 'C:\existing-cache'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' -ErrorAction SilentlyContinue
        }
    }

    It 'preserves empty PLAYWRIGHT_BROWSERS_PATH state' {
        $root = New-InstallTestRoot
        $browsersRoot = Join-Path $root 'browsers'
        $scriptPath = Join-Path $root 'playwright.ps1'
        try {
            $env:PLAYWRIGHT_BROWSERS_PATH = ''
            New-FakePlaywrightInstallScript -Path $scriptPath

            $null = Invoke-PlaywrightChromiumInstall -PlaywrightScriptPath $scriptPath -BrowsersRoot $browsersRoot

            Test-Path -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' | Should -BeTrue
            $env:PLAYWRIGHT_BROWSERS_PATH | Should -Be ''
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' -ErrorAction SilentlyContinue
        }
    }

    It 'restores PLAYWRIGHT_BROWSERS_PATH when install fails' {
        $root = New-InstallTestRoot
        $browsersRoot = Join-Path $root 'browsers'
        $scriptPath = Join-Path $root 'playwright.ps1'
        try {
            $env:PLAYWRIGHT_BROWSERS_PATH = 'C:\existing-cache'
            New-FakePlaywrightInstallScript -Path $scriptPath -ExitCode 7

            { Invoke-PlaywrightChromiumInstall -PlaywrightScriptPath $scriptPath -BrowsersRoot $browsersRoot } |
                Should -Throw '*فشل تنزيل Chromium*'

            $env:PLAYWRIGHT_BROWSERS_PATH | Should -Be 'C:\existing-cache'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'Env:PLAYWRIGHT_BROWSERS_PATH' -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Get-RelativePathFromDirectory' {
    BeforeAll {
        $script:IsWindowsPlatform = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

        function script:New-RelativePathTestRoot {
            $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
            $path = Join-Path $base ("uqeb-relative-path-" + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $path -Force | Out-Null
            return $path
        }
    }

    It 'returns file directly under root' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            Get-RelativePathFromDirectory -RootDirectory $root -FullPath $file |
                Should -Be 'chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns nested file with forward slashes' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chromium-1\chrome-win64\chrome.exe'
            New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            Get-RelativePathFromDirectory -RootDirectory $root -FullPath $file |
                Should -Be 'chromium-1/chrome-win64/chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'accepts root with trailing separator' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            $rootWithSeparator = $root + [System.IO.Path]::DirectorySeparatorChar
            Get-RelativePathFromDirectory -RootDirectory $rootWithSeparator -FullPath $file |
                Should -Be 'chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'accepts root without trailing separator' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            Get-RelativePathFromDirectory -RootDirectory $root.TrimEnd('\', '/') -FullPath $file |
                Should -Be 'chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns empty string when full path equals root' {
        $root = New-RelativePathTestRoot
        try {
            Get-RelativePathFromDirectory -RootDirectory $root -FullPath $root |
                Should -Be ''
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects sibling-prefix bypass paths' {
        $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
        $root = Join-Path $base ("uqeb-relative-foo-" + [Guid]::NewGuid().ToString('N'))
        $outsideDir = "$root-extra"
        $outsideFile = Join-Path $outsideDir 'chrome.exe'
        try {
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            New-Item -ItemType Directory -Path $outsideDir -Force | Out-Null
            Set-Content -LiteralPath $outsideFile -Value 'fake' -Encoding ASCII
            { Get-RelativePathFromDirectory -RootDirectory $root -FullPath $outsideFile } |
                Should -Throw '*يخرج عن الجذر*'
        }
        finally {
            Remove-Item -LiteralPath $outsideDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects paths outside root' {
        $root = New-RelativePathTestRoot
        $outsideRoot = New-RelativePathTestRoot
        try {
            $outsideFile = Join-Path $outsideRoot 'chrome.exe'
            Set-Content -LiteralPath $outsideFile -Value 'fake' -Encoding ASCII
            { Get-RelativePathFromDirectory -RootDirectory $root -FullPath $outsideFile } |
                Should -Throw '*يخرج عن الجذر*'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $outsideRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'accepts differing path casing on Windows' -Skip:(-not $script:IsWindowsPlatform) {
        $root = New-RelativePathTestRoot
        try {
            $mixedCaseRoot = $root.Substring(0, 1).ToUpperInvariant() + $root.Substring(1)
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            Get-RelativePathFromDirectory -RootDirectory $mixedCaseRoot -FullPath $file |
                Should -Be 'chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'does not return a leading separator' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            $result = Get-RelativePathFromDirectory -RootDirectory $root -FullPath $file
            $result.StartsWith('/') | Should -BeFalse
            $result.StartsWith('\') | Should -BeFalse
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'does not return parent traversal segments' {
        $root = New-RelativePathTestRoot
        try {
            $file = Join-Path $root 'chrome.exe'
            Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII
            $result = Get-RelativePathFromDirectory -RootDirectory $root -FullPath $file
            $result | Should -Not -Match '\.\.'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Playwright package hash validation' {
    It 'accepts matching playwright.ps1 hash' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest -PlaywrightScriptSha256 $fixture.ScriptHash
            {
                Assert-PlaywrightPackageHashes `
                    -PlaywrightScriptPath $fixture.PlaywrightScript `
                    -ChromiumExecutablePath $fixture.Executable `
                    -PlaywrightManifest $manifest.playwright
            } | Should -Not -Throw
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects mismatched playwright.ps1 hash' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest -PlaywrightScriptSha256 ('0' * 64)
            {
                Assert-PlaywrightPackageHashes `
                    -PlaywrightScriptPath $fixture.PlaywrightScript `
                    -ChromiumExecutablePath $fixture.Executable `
                    -PlaywrightManifest $manifest.playwright
            } | Should -Throw '*playwright.ps1*'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'accepts matching Chromium hash' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest -BrowserExecutableSha256 $fixture.BrowserHash
            {
                Assert-PlaywrightPackageHashes `
                    -PlaywrightScriptPath $fixture.PlaywrightScript `
                    -ChromiumExecutablePath $fixture.Executable `
                    -PlaywrightManifest $manifest.playwright
            } | Should -Not -Throw
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects mismatched Chromium hash' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest -BrowserExecutableSha256 ('f' * 64)
            {
                Assert-PlaywrightPackageHashes `
                    -PlaywrightScriptPath $fixture.PlaywrightScript `
                    -ChromiumExecutablePath $fixture.Executable `
                    -PlaywrightManifest $manifest.playwright
            } | Should -Throw '*Chromium*'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'skips hash validation when optional hashes are absent' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest
            {
                Assert-PlaywrightPackageHashes `
                    -PlaywrightScriptPath $fixture.PlaywrightScript `
                    -ChromiumExecutablePath $fixture.Executable `
                    -PlaywrightManifest $manifest.playwright
            } | Should -Not -Throw
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Playwright package preflight' {
    It 'passes when package layout and hashes are valid' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest `
                -PlaywrightScriptSha256 $fixture.ScriptHash `
                -BrowserExecutableSha256 $fixture.BrowserHash
            $result = Test-PlaywrightPackagePreflight -PackageRoot $fixture.Root -Manifest $manifest
            $result.ExecutableRelativePath | Should -Be 'chromium-1\chrome-win64\chrome.exe'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects manifest missing playwright section' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = [pscustomobject]@{ applicationName = 'Uqeb' }
            { Test-PlaywrightPackagePreflight -PackageRoot $fixture.Root -Manifest $manifest } |
                Should -Throw '*playwright*'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects missing Chromium executable' {
        $fixture = New-PlaywrightPackageFixture
        try {
            Remove-Item -LiteralPath $fixture.Executable -Force
            $manifest = New-PlaywrightPackageManifest
            { Test-PlaywrightPackagePreflight -PackageRoot $fixture.Root -Manifest $manifest } |
                Should -Throw '*Chromium*'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects unsafe executable path' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $browserManifest = [ordered]@{
                executableRelativePath = '..\outside\chrome.exe'
            }
            ($browserManifest | ConvertTo-Json -Depth 3) |
                Set-Content -LiteralPath (Join-Path $fixture.Root 'browsers\playwright-browser-manifest.json') -Encoding UTF8
            $manifest = New-PlaywrightPackageManifest
            { Test-PlaywrightPackagePreflight -PackageRoot $fixture.Root -Manifest $manifest } |
                Should -Throw
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects mismatched package hashes during preflight' {
        $fixture = New-PlaywrightPackageFixture
        try {
            $manifest = New-PlaywrightPackageManifest `
                -PlaywrightScriptSha256 $fixture.ScriptHash `
                -BrowserExecutableSha256 ('a' * 64)
            { Test-PlaywrightPackagePreflight -PackageRoot $fixture.Root -Manifest $manifest } |
                Should -Throw '*Chromium*'
        }
        finally {
            Remove-Item -LiteralPath $fixture.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'build-production-package.ps1 playwright policy' {
    It 'includes chromium download via published playwright.ps1 by default' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match 'Invoke-PlaywrightChromiumInstall'
        $content | Should -Match 'playwright\.ps1'
        $content | Should -Match 'SkipPlaywrightBrowserDownload'
        $content | Should -Match 'PlaywrightBrowsersSourcePath'
        $content | Should -Not -Match 'npx playwright install'
    }

    It 'requires PlaywrightBrowsersSourcePath when skip download is enabled' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match 'PlaywrightBrowsersSourcePath'
        $content | Should -Match 'Assert-PlaywrightBrowserSourcePathSafe'
    }

    It 'prefixes browser executable path with browsers folder in manifest files' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match "browserRelativePath = 'browsers\\'"
    }

    It 'includes browsers folder in package manifest' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1') -Raw
        $content | Should -Match 'browsers\\playwright-browser-manifest\.json'
        $content | Should -Match 'playwright'
    }
}

Describe 'Playwright browser source copy policy' {
    It 'rejects skip download without source path at parameter validation time' {
        $buildScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'build-production-package.ps1'
        $content = Get-Content -LiteralPath $buildScript -Raw
        $content | Should -Match 'يجب تحديد PlaywrightBrowsersSourcePath'
    }

    It 'copies valid source into staging browsers without deleting source' {
        $root = New-PlaywrightTestDirectory
        $source = Join-Path $root 'cache-browsers'
        $staging = Join-Path $root 'staging\browsers'
        try {
            New-FakeChromiumTree -Root $source | Out-Null
            Ensure-Directory $staging
            Invoke-RobocopySafe -Source $source -Destination $staging -TargetType Generic
            $executable = Get-PlaywrightBrowserExecutable -BrowsersRoot $staging
            $executable.RelativePath | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath (Join-Path $source 'chromium-1\chrome-win64\chrome.exe') | Should -BeTrue
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects source inside temp staging root' {
        $root = New-PlaywrightTestDirectory
        $stagingRoot = Join-Path $root 'temp-staging'
        $source = Join-Path $stagingRoot 'cache'
        $destination = Join-Path $stagingRoot 'browsers'
        try {
            New-Item -ItemType Directory -Path $source -Force | Out-Null
            {
                Assert-PlaywrightBrowserSourcePathSafe `
                    -SourcePath $source `
                    -StagingBrowsersPath $destination `
                    -TempStagingRoot $stagingRoot
            } | Should -Throw '*staging*'
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'install-production-package.ps1 playwright policy' {
    It 'validates playwright payload before database backup' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $match = [regex]::Match($content, 'Test-PlaywrightPackagePreflight[\s\S]*Invoke-ProductionDatabaseBackup')
        $match.Success | Should -BeTrue
    }

    It 'runs automatically detected missing migrations after stopping API service' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $match = [regex]::Match(
            $content,
            'Test-RequiredMigrationPresent[\s\S]+?Stop-ScheduledTask\s+-TaskName[\s\S]+?if\s*\(\$requiredMigrationMissing\)[\s\S]+?&\s+\$migrationScript\b[\s\S]+?Test-RequiredMigrationApplied[\s\S]+?Install-StagedReleaseToProduction'
        )
        $match.Success | Should -BeTrue
    }

    It 'sets PLAYWRIGHT_BROWSERS_PATH via run-api wrapper' {
        $content = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'install-production-package.ps1') -Raw
        $content | Should -Match 'Write-ApiRunScript'
        $content | Should -Match 'PlaywrightBrowsersPath'
    }
}
