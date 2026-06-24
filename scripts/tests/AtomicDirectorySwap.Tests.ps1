BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-AtomicSwapRoot {
        $base = if ([string]::IsNullOrWhiteSpace($env:TEMP)) { '/tmp' } else { $env:TEMP }
        $path = Join-Path $base ("uqeb-atomic-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function script:Initialize-AtomicSwapSource {
        param([string]$Source)

        New-Item -ItemType Directory -Path $Source -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Source 'marker.txt') -Value 'source-content' -Encoding ASCII
    }
}

Describe 'Copy-DirectoryAtomically rollback' {
    BeforeEach {
        $script:Root = New-AtomicSwapRoot
        $script:Source = Join-Path $Root 'source'
        $script:Target = Join-Path $Root 'target'
        Initialize-AtomicSwapSource -Source $Source

        Mock Invoke-RobocopySafe {
            param(
                [string]$Source,
                [string]$Destination
            )

            if (Test-Path -LiteralPath $Destination) {
                Remove-Item -LiteralPath $Destination -Recurse -Force
            }

            Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
        }

        Mock Test-PlaywrightBrowserPayload { }
    }

    AfterEach {
        Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'swaps staging into target and preserves previous directory' {
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Target 'old.txt') -Value 'old' -Encoding ASCII

        $previous = Copy-DirectoryAtomically -Source $Source -Target $Target

        $previous | Should -Be "$Target.previous"
        Test-Path -LiteralPath (Join-Path $Target 'marker.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $previous 'old.txt') | Should -BeTrue
    }

    It 'restores previous target when staging move fails' {
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Target 'old.txt') -Value 'old' -Encoding ASCII

        Mock Move-Item -ParameterFilter { $LiteralPath -like '*.next' } {
            throw 'simulated staging move failure'
        }

        { Copy-DirectoryAtomically -Source $Source -Target $Target } |
            Should -Throw 'simulated staging move failure'

        Test-Path -LiteralPath (Join-Path $Target 'old.txt') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $Target 'marker.txt') | Should -BeFalse
    }

    It 'removes partial target before restoring previous directory' {
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Target 'old.txt') -Value 'old' -Encoding ASCII

        Mock Move-Item -ParameterFilter { $LiteralPath -like '*.next' } {
            param(
                [string]$LiteralPath,
                [string]$Destination
            )

            if (-not (Test-Path -LiteralPath $Destination)) {
                New-Item -ItemType Directory -Path $Destination -Force | Out-Null
            }

            Set-Content -LiteralPath (Join-Path $Destination 'partial.txt') -Value 'partial' -Encoding ASCII
            throw 'simulated staging move failure'
        }

        { Copy-DirectoryAtomically -Source $Source -Target $Target } |
            Should -Throw 'simulated staging move failure'

        Test-Path -LiteralPath (Join-Path $Target 'partial.txt') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $Target 'old.txt') | Should -BeTrue
    }

    It 'throws compound error when restore fails' {
        New-Item -ItemType Directory -Path $Target -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Target 'old.txt') -Value 'old' -Encoding ASCII

        Mock Move-Item -ParameterFilter { $LiteralPath -like '*.next' } {
            throw 'staging failed'
        }

        Mock Move-Item -ParameterFilter { $LiteralPath -like '*.previous' } {
            throw 'restore failed'
        }

        { Copy-DirectoryAtomically -Source $Source -Target $Target } |
            Should -Throw '*فشل استبدال المجلد وفشل استعادة النسخة السابقة*'
    }

    It 'does not attempt restore when no previous target existed' {
        Mock Move-Item -ParameterFilter { $LiteralPath -like '*.next' } {
            throw 'staging failed without previous target'
        }

        { Copy-DirectoryAtomically -Source $Source -Target $Target } |
            Should -Throw 'staging failed without previous target'

        Test-Path -LiteralPath $Target | Should -BeFalse
    }

    It 'installs staging when target does not exist' {
        $previous = Copy-DirectoryAtomically -Source $Source -Target $Target

        $previous | Should -BeNullOrEmpty
        Test-Path -LiteralPath (Join-Path $Target 'marker.txt') | Should -BeTrue
    }
}
