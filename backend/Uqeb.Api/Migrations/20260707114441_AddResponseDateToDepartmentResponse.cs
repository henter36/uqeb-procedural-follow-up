using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResponseDateToDepartmentResponse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDate",
                table: "DepartmentResponses",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseDate",
                table: "DepartmentResponses");
        }
    }
}
