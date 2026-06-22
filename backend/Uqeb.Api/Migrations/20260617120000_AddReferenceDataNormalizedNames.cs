using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260617120000_AddReferenceDataNormalizedNames")]
public partial class AddReferenceDataNormalizedNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NameNormalized",
            table: "Departments",
            type: "nvarchar(450)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "NameNormalized",
            table: "ExternalParties",
            type: "nvarchar(450)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "NameNormalized",
            table: "Categories",
            type: "nvarchar(450)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql("""
            UPDATE Departments SET NameNormalized = LOWER(LTRIM(RTRIM(Name)));
            UPDATE ExternalParties SET NameNormalized = LOWER(LTRIM(RTRIM(Name)));
            UPDATE Categories SET NameNormalized = LOWER(LTRIM(RTRIM(Name)));
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Departments_NameNormalized",
            table: "Departments",
            column: "NameNormalized",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExternalParties_NameNormalized",
            table: "ExternalParties",
            column: "NameNormalized",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Categories_NameNormalized",
            table: "Categories",
            column: "NameNormalized",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true,
            filter: "[Email] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Users_Email", table: "Users");
        migrationBuilder.DropIndex(name: "IX_Categories_NameNormalized", table: "Categories");
        migrationBuilder.DropIndex(name: "IX_ExternalParties_NameNormalized", table: "ExternalParties");
        migrationBuilder.DropIndex(name: "IX_Departments_NameNormalized", table: "Departments");

        migrationBuilder.DropColumn(name: "NameNormalized", table: "Categories");
        migrationBuilder.DropColumn(name: "NameNormalized", table: "ExternalParties");
        migrationBuilder.DropColumn(name: "NameNormalized", table: "Departments");
    }
}
