using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceDataNormalizedNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "ExternalParties",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Departments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameNormalized",
                table: "Categories",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
            UPDATE Departments
            SET NameNormalized = LOWER(
                LTRIM(
                    RTRIM(
                        TRANSLATE(
                            Name,
                            NCHAR(9) +
                            NCHAR(10) +
                            NCHAR(11) +
                            NCHAR(12) +
                            NCHAR(13) +
                            NCHAR(133) +
                            NCHAR(160) +
                            NCHAR(5760) +
                            NCHAR(8192) +
                            NCHAR(8193) +
                            NCHAR(8194) +
                            NCHAR(8195) +
                            NCHAR(8196) +
                            NCHAR(8197) +
                            NCHAR(8198) +
                            NCHAR(8199) +
                            NCHAR(8200) +
                            NCHAR(8201) +
                            NCHAR(8202) +
                            NCHAR(8232) +
                            NCHAR(8233) +
                            NCHAR(8239) +
                            NCHAR(8287) +
                            NCHAR(12288),
                            REPLICATE(N' ', 24)
                        )
                    )
                )
            );

            WHILE EXISTS (
                SELECT 1
                FROM Departments
                WHERE NameNormalized LIKE N'%  %'
            )
            BEGIN
                UPDATE Departments
                SET NameNormalized = REPLACE(NameNormalized, N'  ', N' ')
                WHERE NameNormalized LIKE N'%  %';
            END;

            UPDATE ExternalParties
            SET NameNormalized = LOWER(
                LTRIM(
                    RTRIM(
                        TRANSLATE(
                            Name,
                            NCHAR(9) +
                            NCHAR(10) +
                            NCHAR(11) +
                            NCHAR(12) +
                            NCHAR(13) +
                            NCHAR(133) +
                            NCHAR(160) +
                            NCHAR(5760) +
                            NCHAR(8192) +
                            NCHAR(8193) +
                            NCHAR(8194) +
                            NCHAR(8195) +
                            NCHAR(8196) +
                            NCHAR(8197) +
                            NCHAR(8198) +
                            NCHAR(8199) +
                            NCHAR(8200) +
                            NCHAR(8201) +
                            NCHAR(8202) +
                            NCHAR(8232) +
                            NCHAR(8233) +
                            NCHAR(8239) +
                            NCHAR(8287) +
                            NCHAR(12288),
                            REPLICATE(N' ', 24)
                        )
                    )
                )
            );

            WHILE EXISTS (
                SELECT 1
                FROM ExternalParties
                WHERE NameNormalized LIKE N'%  %'
            )
            BEGIN
                UPDATE ExternalParties
                SET NameNormalized = REPLACE(NameNormalized, N'  ', N' ')
                WHERE NameNormalized LIKE N'%  %';
            END;

            UPDATE Categories
            SET NameNormalized = LOWER(
                LTRIM(
                    RTRIM(
                        TRANSLATE(
                            Name,
                            NCHAR(9) +
                            NCHAR(10) +
                            NCHAR(11) +
                            NCHAR(12) +
                            NCHAR(13) +
                            NCHAR(133) +
                            NCHAR(160) +
                            NCHAR(5760) +
                            NCHAR(8192) +
                            NCHAR(8193) +
                            NCHAR(8194) +
                            NCHAR(8195) +
                            NCHAR(8196) +
                            NCHAR(8197) +
                            NCHAR(8198) +
                            NCHAR(8199) +
                            NCHAR(8200) +
                            NCHAR(8201) +
                            NCHAR(8202) +
                            NCHAR(8232) +
                            NCHAR(8233) +
                            NCHAR(8239) +
                            NCHAR(8287) +
                            NCHAR(12288),
                            REPLICATE(N' ', 24)
                        )
                    )
                )
            );

            WHILE EXISTS (
                SELECT 1
                FROM Categories
                WHERE NameNormalized LIKE N'%  %'
            )
            BEGIN
                UPDATE Categories
                SET NameNormalized = REPLACE(NameNormalized, N'  ', N' ')
                WHERE NameNormalized LIKE N'%  %';
            END;
            """);

            migrationBuilder.Sql(
                """
            IF EXISTS (
                SELECT 1
                FROM Departments
                WHERE NameNormalized = N''
            )
            BEGIN
                THROW 51001, N'One or more departments have an empty normalized name.', 1;
            END;

            IF EXISTS (
                SELECT 1
                FROM ExternalParties
                WHERE NameNormalized = N''
            )
            BEGIN
                THROW 51002, N'One or more external parties have an empty normalized name.', 1;
            END;

            IF EXISTS (
                SELECT 1
                FROM Categories
                WHERE NameNormalized = N''
            )
            BEGIN
                THROW 51003, N'One or more categories have an empty normalized name.', 1;
            END;
            """);

            migrationBuilder.Sql(
                """
            IF EXISTS (
                SELECT NameNormalized
                FROM Departments
                GROUP BY NameNormalized
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51011, N'Duplicate normalized department names exist.', 1;
            END;

            IF EXISTS (
                SELECT NameNormalized
                FROM ExternalParties
                GROUP BY NameNormalized
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51012, N'Duplicate normalized external party names exist.', 1;
            END;

            IF EXISTS (
                SELECT NameNormalized
                FROM Categories
                GROUP BY NameNormalized
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51013, N'Duplicate normalized category names exist.', 1;
            END;
            """);

            migrationBuilder.Sql(
                """
            UPDATE Users
            SET Email = NULL
            WHERE Email IS NOT NULL
              AND LTRIM(RTRIM(Email)) = N'';

            UPDATE Users
            SET Email = LTRIM(RTRIM(Email))
            WHERE Email IS NOT NULL;

            UPDATE Users
            SET Email = LOWER(Email)
            WHERE Email IS NOT NULL;
            """);

            migrationBuilder.Sql(
                """
            IF EXISTS (
                SELECT 1
                FROM Users
                WHERE Email IS NOT NULL
                  AND DATALENGTH(Email) > 900
            )
            BEGIN
                THROW 51015, N'One or more user email addresses exceed 450 characters after normalization.', 1;
            END;
            """);

            migrationBuilder.Sql(
                """
            IF EXISTS (
                SELECT Email
                FROM Users
                WHERE Email IS NOT NULL
                GROUP BY Email
                HAVING COUNT(*) > 1
            )
            BEGIN
                THROW 51014, N'Duplicate user email addresses exist.', 1;
            END;
            """);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalParties_NameNormalized",
                table: "ExternalParties",
                column: "NameNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_NameNormalized",
                table: "Departments",
                column: "NameNormalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NameNormalized",
                table: "Categories",
                column: "NameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_ExternalParties_NameNormalized",
                table: "ExternalParties");

            migrationBuilder.DropIndex(
                name: "IX_Departments_NameNormalized",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_NameNormalized",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "ExternalParties");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "NameNormalized",
                table: "Categories");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }

}
