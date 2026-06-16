using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uqeb.Api.Data;

#nullable disable

namespace Uqeb.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260615120000_AddAuthSecurityAuditLogs")]
    public class AddAuthSecurityAuditLogs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginAttemptLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RiskLevel = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "low"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttemptLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAlerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttemptLogs_OccurredAt",
                table: "LoginAttemptLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttemptLogs_Username_OccurredAt",
                table: "LoginAttemptLogs",
                columns: new[] { "Username", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttemptLogs_IpAddress_OccurredAt",
                table: "LoginAttemptLogs",
                columns: new[] { "IpAddress", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttemptLogs_Succeeded_OccurredAt",
                table: "LoginAttemptLogs",
                columns: new[] { "Succeeded", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAlerts_CreatedAt",
                table: "SecurityAlerts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAlerts_IsRead_CreatedAt",
                table: "SecurityAlerts",
                columns: new[] { "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAlerts_Type_CreatedAt",
                table: "SecurityAlerts",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAlerts_Severity_CreatedAt",
                table: "SecurityAlerts",
                columns: new[] { "Severity", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LoginAttemptLogs");
            migrationBuilder.DropTable(name: "SecurityAlerts");
        }
    }
}
