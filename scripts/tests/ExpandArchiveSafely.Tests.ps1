#Requires -Version 5.1

BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-ArchiveTestDirectory {
        $path = Join-Path $env:TEMP ("uqeb-zip-slip-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        return $path
    }

    function script:New-TestZipArchive {
        param(
            [hashtable]$Entries,
            [string]$ZipPath
        )

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if (Test-Path -LiteralPath $ZipPath) {
            Remove-Item -LiteralPath $ZipPath -Force
        }

        $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in $Entries.GetEnumerator()) {
                $zipEntry = $zip.CreateEntry($entry.Key)
                $stream = $zipEntry.Open()
                try {
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$entry.Value)
                    $stream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $stream.Dispose()
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
}

Describe 'Archive path helpers' {
    It 'rejects traversal segments' {
        Test-ArchiveEntryNameIsUnsafe -EntryName '..\outside.txt' | Should -BeTrue
        Test-ArchiveEntryNameIsUnsafe -EntryName 'safe\sub\file.txt' | Should -BeFalse
    }

    It 'rejects rooted and drive-qualified paths' {
        Test-ArchiveEntryNameIsUnsafe -EntryName 'C:\outside.txt' | Should -BeTrue
        Test-ArchiveEntryNameIsUnsafe -EntryName '\outside.txt' | Should -BeTrue
        Test-ArchiveEntryNameIsUnsafe -EntryName '\\server\share\file.txt' | Should -BeTrue
    }

    It 'builds destination root with trailing separator' {
        $root = New-ArchiveTestDirectory
        try {
            $safeRoot = Get-SafeArchiveDestinationRoot -DestinationPath $root
            $safeRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar) | Should -BeTrue
        }
        finally {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects sibling-prefix bypass for C:\foo-extra' {
        $destination = New-ArchiveTestDirectory
        try {
            $safeRoot = Get-SafeArchiveDestinationRoot -DestinationPath $destination
            {
                Resolve-SafeArchiveTargetPath -DestinationRoot $safeRoot -EntryName '..\foo-extra\file.txt'
            } | Should -Throw '*غير آمن*'
        }
        finally {
            Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Expand-ArchiveSafely' {
    It 'extracts only safe entries' {
        $destination = New-ArchiveTestDirectory
        $zipPath = Join-Path $destination 'package.zip'
        $extractRoot = Join-Path $destination 'extract'
        New-TestZipArchive -ZipPath $zipPath -Entries @{
            'safe\sub\file.txt' = 'ok'
            'safe-directory-entry/' = ''
        }

        Expand-ArchiveSafely -ArchivePath $zipPath -DestinationPath $extractRoot

        Test-Path -LiteralPath (Join-Path $extractRoot 'safe\sub\file.txt') | Should -BeTrue
        (Get-Content -LiteralPath (Join-Path $extractRoot 'safe\sub\file.txt') -Raw) | Should -Be 'ok'
    }

    It 'rejects malicious zip entries' {
        foreach ($entryName in @(
            '..\outside.txt',
            'C:\outside.txt',
            '\\server\share\file.txt'
        )) {
            $destination = New-ArchiveTestDirectory
            $zipPath = Join-Path $destination 'evil.zip'
            $extractRoot = Join-Path $destination 'extract'
            New-TestZipArchive -ZipPath $zipPath -Entries @{ $entryName = 'evil' }

            {
                Expand-ArchiveSafely -ArchivePath $zipPath -DestinationPath $extractRoot
            } | Should -Throw

            Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects prefix bypass entry C:\foo-extra\file.txt when destination is C:\foo' {
        $destination = New-ArchiveTestDirectory
        $zipPath = Join-Path $destination 'bypass.zip'
        $extractRoot = Join-Path $destination 'foo'
        New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
        New-TestZipArchive -ZipPath $zipPath -Entries @{
            '..\foo-extra\file.txt' = 'bypass'
        }

        {
            Expand-ArchiveSafely -ArchivePath $zipPath -DestinationPath $extractRoot
        } | Should -Throw '*غير آمن*'
    }
}
