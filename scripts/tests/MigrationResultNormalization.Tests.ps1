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

Describe 'Get-DeploymentReportLatestMigrationId' {
    It 'returns only the migration id as a single string when reading succeeds' {
        Mock Get-LatestAppliedMigrationId {
            return '20260628190617_AddDepartmentResponseWorkflow'
        }

        $result = Get-DeploymentReportLatestMigrationId -ConnectionString 'Server=.;Database=Uqeb;Trusted_Connection=True;'

        $result | Should -Be '20260628190617_AddDepartmentResponseWorkflow'
        $result | Should -BeOfType [string]
        $result | Should -Not -Match 'تعذر قراءة آخر migration'
        @($result).Count | Should -Be 1
    }

    It 'returns unknown as a single string when reading fails without polluting the success pipeline' {
        Mock Get-LatestAppliedMigrationId {
            throw 'SQL history unavailable'
        }

        $informationPath = Join-Path $TestDrive 'migration-report-info.txt'
        $result = Get-DeploymentReportLatestMigrationId -ConnectionString 'bad' 6> $informationPath

        $result | Should -Be 'غير معروف'
        $result | Should -BeOfType [string]
        $result | Should -Not -Match 'تعذر قراءة آخر migration'
        @($result).Count | Should -Be 1
        (Get-Content -LiteralPath $informationPath -Raw) | Should -Match 'تعذر قراءة آخر migration'
    }

    It 'does not pollute assignment values when migration reading fails' {
        Mock Get-LatestAppliedMigrationId {
            throw 'SQL history unavailable'
        }

        $migrationAfterRollback = Get-DeploymentReportLatestMigrationId -ConnectionString 'bad'

        $migrationAfterRollback | Should -Be 'غير معروف'
        $migrationAfterRollback | Should -BeOfType [string]
        @($migrationAfterRollback).Count | Should -Be 1
    }
}
