BeforeAll {
    $script:ScriptsRoot = Split-Path $PSScriptRoot -Parent
    $script:CommonPath  = Join-Path $ScriptsRoot 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-TempDir {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("uqeb-transfer-test-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function script:New-FakeZip {
        param([string]$DestDir, [string]$Version = "20260101-000000")
        $zipName = "Uqeb-$Version.zip"
        $zipPath = Join-Path $DestDir $zipName
        # Create a minimal valid ZIP with manifest.json
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $ms  = New-Object System.IO.MemoryStream
        $zip = New-Object System.IO.Compression.ZipArchive($ms, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        $entry = $zip.CreateEntry('manifest.json')
        $sw    = New-Object System.IO.StreamWriter($entry.Open())
        $sw.Write((@{
            version                  = $Version
            commitSha                = 'abc1234'
            minimumDatabaseMigration = '20260625194203_AddFollowUpPrintQueueAndLetterTemplatesV2'
        } | ConvertTo-Json -Compress))
        $sw.Dispose()
        $zip.Dispose()
        [System.IO.File]::WriteAllBytes($zipPath, $ms.ToArray())
        $ms.Dispose()

        $hash = Get-FileSha256Hex -Path $zipPath
        $shaPath = "$zipPath.sha256.txt"
        Set-Content -LiteralPath $shaPath -Value $hash -Encoding ASCII
        return [pscustomobject]@{ ZipPath = $zipPath; ShaPath = $shaPath; Version = $Version }
    }
}

Describe 'prepare-production-transfer.ps1: basic structure' {
    BeforeEach {
        $script:TempRoot    = New-TempDir
        $script:ArtifactDir = Join-Path $TempRoot "artifacts\production"
        New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null

        $script:TransferRoot = Join-Path $TempRoot "artifacts\transfer"
        $script:Pkg = New-FakeZip -DestDir $ArtifactDir -Version "20260101-120000"
    }

    AfterEach {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'creates transfer directory with incoming and tools subfolders' {
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
            -TransferRoot $TransferRoot

        $transferDir = Join-Path $TransferRoot "UqebDeploy-20260101-120000"
        Test-Path -LiteralPath (Join-Path $transferDir 'incoming') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $transferDir 'tools')    | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $transferDir 'tools\deployment\Common.ps1') | Should -BeTrue
    }

    It 'copies ZIP and SHA256 into incoming' {
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
            -TransferRoot $TransferRoot

        $transferDir = Join-Path $TransferRoot "UqebDeploy-20260101-120000"
        $incomingDir = Join-Path $transferDir 'incoming'
        $zips = @(Get-ChildItem -LiteralPath $incomingDir -Filter '*.zip')
        $shas = @(Get-ChildItem -LiteralPath $incomingDir -Filter '*.sha256.txt')
        $zips.Count | Should -Be 1
        $shas.Count | Should -Be 1
    }

    It 'creates deploy.ps1 launcher at transfer root' {
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
            -TransferRoot $TransferRoot

        $transferDir = Join-Path $TransferRoot "UqebDeploy-20260101-120000"
        Test-Path -LiteralPath (Join-Path $transferDir 'deploy.ps1') | Should -BeTrue
    }

    It 'creates TRANSFER-README.txt' {
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
            -TransferRoot $TransferRoot

        $transferDir = Join-Path $TransferRoot "UqebDeploy-20260101-120000"
        Test-Path -LiteralPath (Join-Path $transferDir 'TRANSFER-README.txt') | Should -BeTrue
    }

    It 'fails when artifacts\production does not exist' {
        $emptyRoot = Join-Path $TempRoot "empty"
        {
            & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
                -ArtifactsRoot $emptyRoot `
                -TransferRoot $TransferRoot
        } | Should -Throw
    }

    It 'fails when SHA256 does not match tampered ZIP' {
        # Tamper the zip
        $bytes = [System.IO.File]::ReadAllBytes($Pkg.ZipPath)
        $bytes[0] = 0xFF
        [System.IO.File]::WriteAllBytes($Pkg.ZipPath, $bytes)

        {
            & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
                -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
                -TransferRoot $TransferRoot
        } | Should -Throw
    }

    It 'accepts explicit ZipPath parameter' {
        $transferRoot2 = Join-Path $TempRoot "xfer2"
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ZipPath $Pkg.ZipPath `
            -TransferRoot $transferRoot2

        $dirs = @(Get-ChildItem -LiteralPath $transferRoot2 -Directory)
        $dirs.Count | Should -Be 1
        Test-Path -LiteralPath (Join-Path $dirs[0].FullName 'incoming') | Should -BeTrue
    }

    It 'transfer package does not reference C:\Users\alqud\uqeb' {
        & (Join-Path $ScriptsRoot 'prepare-production-transfer.ps1') `
            -ArtifactsRoot (Join-Path $TempRoot 'artifacts') `
            -TransferRoot $TransferRoot

        $transferDir = Join-Path $TransferRoot "UqebDeploy-20260101-120000"
        $allText = Get-ChildItem -LiteralPath $transferDir -Recurse -File |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue }
        $combined = $allText -join "`n"
        $combined | Should -Not -Match 'C:\\Users\\alqud\\uqeb'
    }
}
