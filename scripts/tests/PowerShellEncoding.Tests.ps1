#Requires -Version 5.1

Describe 'PowerShell script encoding policy' {
    It 'requires UTF-8 BOM when a PowerShell script contains non-ASCII bytes' {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $scriptFiles = @(
            Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Include '*.ps1', '*.psm1' |
                Where-Object {
                    $_.FullName -notmatch '[\\/](\.git|node_modules|bin|obj|artifacts)[\\/]'
                }
        )

        $violations = New-Object System.Collections.Generic.List[string]
        foreach ($file in $scriptFiles) {
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            $hasNonAscii = $false
            foreach ($byte in $bytes) {
                if ($byte -gt 127) {
                    $hasNonAscii = $true
                    break
                }
            }

            if (-not $hasNonAscii) {
                continue
            }

            $hasUtf8Bom = (
                $bytes.Length -ge 3 -and
                $bytes[0] -eq 0xEF -and
                $bytes[1] -eq 0xBB -and
                $bytes[2] -eq 0xBF
            )

            if (-not $hasUtf8Bom) {
                [void]$violations.Add($file.FullName.Substring($repoRoot.Length + 1))
            }
        }

        $violations | Should -BeNullOrEmpty
    }
}
