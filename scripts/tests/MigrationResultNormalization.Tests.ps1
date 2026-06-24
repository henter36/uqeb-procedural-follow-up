BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-MigrationIdDataTable {
        param([string[]]$MigrationIds)

        $table = New-Object System.Data.DataTable
        [void]$table.Columns.Add('MigrationId', [string])
        foreach ($migrationId in $MigrationIds) {
            $row = $table.NewRow()
            $row.MigrationId = $migrationId
            [void]$table.Rows.Add($row)
        }
        return ,$table
    }
}

Describe 'Resolve-MigrationIdsFromHandlerResult' {
    It 'returns empty array for null' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result $null
        @($result).Count | Should -Be 0
    }

    It 'trims valid string values' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result '  20260101_Test  '
        $result | Should -BeExactly @('20260101_Test')
    }

    It 'returns empty array for blank string' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result '   '
        @($result).Count | Should -Be 0
    }

    It 'normalizes string arrays by trimming and removing blanks' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result @(
            '  20260101_A  '
            ''
            '   '
            $null
            '20260101_B'
        )
        $result | Should -BeExactly @('20260101_A', '20260101_B')
    }

    It 'reads migration ids from DataTable' {
        $table = New-MigrationIdDataTable -MigrationIds @(' 20260101_A ', '20260101_B')
        $result = Resolve-MigrationIdsFromHandlerResult -Result $table
        $result | Should -BeExactly @('20260101_A', '20260101_B')
    }

    It 'reads migration ids from DataRow via parent table' {
        $table = New-MigrationIdDataTable -MigrationIds @('20260101_A', '20260101_B')
        $row = $table.Rows[0]
        $result = Resolve-MigrationIdsFromHandlerResult -Result $row
        $result | Should -BeExactly @('20260101_A', '20260101_B')
    }

    It 'reads migration ids from generic string array' {
        $array = [object[]]@(' 20260101_A ', '20260101_B')
        $result = Resolve-MigrationIdsFromHandlerResult -Result $array
        $result | Should -BeExactly @('20260101_A', '20260101_B')
    }

    It 'reads migration ids from array containing DataTable' {
        $table = New-MigrationIdDataTable -MigrationIds @('20260101_A')
        $array = [object[]]@($table, 'ignored')
        $result = Resolve-MigrationIdsFromHandlerResult -Result $array
        $result | Should -BeExactly @('20260101_A')
    }

    It 'returns empty array for empty array input' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result @()
        @($result).Count | Should -Be 0
    }

    It 'returns empty array for unsupported result types' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result 42
        @($result).Count | Should -Be 0
    }

    It 'keeps single-item results as arrays' {
        $result = Resolve-MigrationIdsFromHandlerResult -Result ' 20260101_A '
        $result -is [System.Array] | Should -BeTrue
        @($result).Count | Should -Be 1
        $result | Should -BeExactly @('20260101_A')
    }
}
