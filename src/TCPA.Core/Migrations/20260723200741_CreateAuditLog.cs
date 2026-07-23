using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs");

            migrationBuilder.RenameTable(
                name: "AuditLogs",
                newName: "AuditLog");

            migrationBuilder.AddColumn<string>(
                name: "AgentId",
                table: "AuditLog",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AnomalyFlag",
                table: "AuditLog",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "AuditLog",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "AuditLog",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "AuditLog",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MessageId",
                table: "AuditLog",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                table: "AuditLog",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "AuditLog",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditLog",
                table: "AuditLog",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_PhoneNumber_OccurredAt",
                table: "AuditLog",
                columns: new[] { "PhoneNumber", "OccurredAt" });

            // DENY DELETE on AuditLog table — audit logs must be immutable
            // IMPORTANT: Confirm the actual SQL Server application login name with DBA
            // and replace 'tcpa_app_user' with the correct login before deploying.
            migrationBuilder.Sql(
                "DENY DELETE ON dbo.AuditLog TO [tcpa_app_user]",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AuditLog",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_PhoneNumber_OccurredAt",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "AnomalyFlag",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "AuditLog");

            migrationBuilder.RenameTable(
                name: "AuditLog",
                newName: "AuditLogs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuditLogs",
                table: "AuditLogs",
                column: "Id");
        }
    }
}
