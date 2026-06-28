BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath
}

Describe 'Repair-IdempotentMigrationScript: Repair 1 — NameNormalized/Departments GO separator' {
    It 'inserts GO between ALTER TABLE NameNormalized and UPDATE Departments (flat pattern)' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL DEFAULT N'';
UPDATE Departments SET NameNormalized = Name;
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        $result | Should -Match '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*GO\s*UPDATE\s+Departments'
    }

    It 'inserts GO for idempotent IF NOT EXISTS pattern' {
        $migrationSql = @"
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'test')
BEGIN
    ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL DEFAULT N'';
END;
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'test')
BEGIN
    UPDATE Departments SET NameNormalized = Name;
END;
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        $result | Should -Match '(?is)ALTER TABLE \[Categories\] ADD \[NameNormalized\][^;]*;\s*END;\s*GO[\s\S]*?UPDATE\s+Departments'
    }

    It 'does not modify already-repaired content (flat)' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL DEFAULT N'';
GO
UPDATE Departments SET NameNormalized = Name;
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        $result | Should -Be $migrationSql
    }
}

Describe 'Repair-IdempotentMigrationScript: Repair 2 — LetterTemplates V2 EXEC wrapping' {
    It 'wraps UPDATE LetterTemplates SET IsDefault in EXEC' {
        $bare = "UPDATE LetterTemplates`nSET IsDefault = 1, TemplateType = 1, SortOrder = 0`nWHERE Code = 'follow_up_letter';"
        $migrationSql = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'migration_v2')
BEGIN
    ALTER TABLE [LetterTemplates] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
    ALTER TABLE [LetterTemplates] ADD [TemplateType] int NOT NULL DEFAULT 0;
    ALTER TABLE [LetterTemplates] ADD [SortOrder] int NOT NULL DEFAULT 0;
    $bare
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'migration_v2', N'9.0.0');
END;
GO
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        # The UPDATE must now be wrapped inside an EXEC()
        $result | Should -Match "(?is)EXEC\s*\(\s*N'UPDATE\s+\[?LetterTemplates\]?"
        # Test-IdempotentMigrationScriptRepaired must confirm both repairs done
        Test-IdempotentMigrationScriptRepaired -Content $result | Should -BeTrue
    }

    It 'does not modify content where UPDATE is already inside EXEC' {
        # Build the EXEC string without confusing here-string quoting
        $execLine = 'EXEC(N' + "'" + 'UPDATE [LetterTemplates] SET [IsDefault] = 1, [TemplateType] = 1, [SortOrder] = 0 WHERE [Code] = N' + "''" + 'follow_up_letter' + "''" + "')"
        $migrationSql = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'migration_v2')
BEGIN
    ALTER TABLE [LetterTemplates] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
    $execLine
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'migration_v2', N'9.0.0');
END;
GO
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        # EXEC must still be present and not duplicated
        $result | Should -Match "(?is)EXEC\s*\(\s*N'UPDATE\s+\[?LetterTemplates\]?"
        ($result -split '(?i)EXEC\s*\(').Count | Should -Be 2  # exactly one EXEC
    }

    It 'does not wrap a LetterTemplates UPDATE that affects a different row (no follow_up_letter)' {
        $migrationSql = @"
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'migration_v3')
BEGIN
    ALTER TABLE [LetterTemplates] ADD [SomeNewColumn] int NOT NULL DEFAULT 0;
    UPDATE [LetterTemplates] SET [SomeNewColumn] = 42 WHERE [Code] = N'other_template';
END;
GO
"@
        $result = Repair-IdempotentMigrationScript -Content $migrationSql
        # The UPDATE for a different row must NOT be wrapped in EXEC
        $result | Should -Not -Match "(?is)EXEC\s*\(\s*N'UPDATE\s+\[?LetterTemplates\]?"
        $result | Should -Be $migrationSql
    }

    It 'returns empty input unchanged' {
        Repair-IdempotentMigrationScript -Content '' | Should -Be ''
        Repair-IdempotentMigrationScript -Content $null | Should -Be $null
    }
}

Describe 'Test-IdempotentMigrationScriptRepaired' {
    It 'returns false for content missing both repairs' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL;
UPDATE Departments SET NameNormalized = Name;
UPDATE LetterTemplates SET IsDefault = 1, TemplateType = 1, SortOrder = 0 WHERE Code = 'follow_up_letter';
"@
        Test-IdempotentMigrationScriptRepaired -Content $migrationSql | Should -BeFalse
    }

    It 'returns false when Repair 1 done but Repair 2 missing' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL;
GO
UPDATE Departments SET NameNormalized = Name;
UPDATE LetterTemplates SET IsDefault = 1, TemplateType = 1, SortOrder = 0 WHERE Code = 'follow_up_letter';
"@
        Test-IdempotentMigrationScriptRepaired -Content $migrationSql | Should -BeFalse
    }

    It 'returns true when both repairs are present' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL;
GO
UPDATE Departments SET NameNormalized = Name;
EXEC(N'UPDATE [LetterTemplates] SET [IsDefault] = 1, [TemplateType] = 1, [SortOrder] = 0 WHERE [Code] = N''follow_up_letter''')
"@
        Test-IdempotentMigrationScriptRepaired -Content $migrationSql | Should -BeTrue
    }

    It 'returns true when V2 UPDATE is absent (no V2 migration in script)' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL;
GO
UPDATE Departments SET NameNormalized = Name;
"@
        Test-IdempotentMigrationScriptRepaired -Content $migrationSql | Should -BeTrue
    }

    It 'returns true when neither repair pattern is present (no applicable migrations)' {
        $migrationSql = @"
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260101_SomeOtherMigration')
BEGIN
    ALTER TABLE [SomeTable] ADD [SomeColumn] nvarchar(100) NULL;
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260101_SomeOtherMigration', N'10.0.0');
END;
GO
"@
        Test-IdempotentMigrationScriptRepaired -Content $migrationSql | Should -BeTrue
    }

    It 'returns false for empty content' {
        Test-IdempotentMigrationScriptRepaired -Content '' | Should -BeFalse
        Test-IdempotentMigrationScriptRepaired -Content $null | Should -BeFalse
    }
}

Describe 'Repair-IdempotentMigrationScript: idempotency' {
    It 'running repair twice produces same result as running once' {
        $migrationSql = @"
ALTER TABLE [Categories] ADD [NameNormalized] nvarchar(200) NOT NULL DEFAULT N'';
UPDATE Departments SET NameNormalized = Name;
UPDATE LetterTemplates SET IsDefault = 1, TemplateType = 1, SortOrder = 0 WHERE Code = 'follow_up_letter';
"@
        $once  = Repair-IdempotentMigrationScript -Content $migrationSql
        $twice = Repair-IdempotentMigrationScript -Content $once
        $twice | Should -Be $once
    }
}
