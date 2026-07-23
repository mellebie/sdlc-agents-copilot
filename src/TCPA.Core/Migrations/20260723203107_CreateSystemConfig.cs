using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateSystemConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SystemConfigs",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "SystemConfigs");

            migrationBuilder.RenameTable(
                name: "SystemConfigs",
                newName: "SystemConfig");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "SystemConfig",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SystemConfig",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SystemConfig",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "SystemConfig",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SystemConfig",
                table: "SystemConfig",
                column: "Key");

            migrationBuilder.InsertData(
                table: "SystemConfig",
                columns: new[] { "Key", "Value", "Description" },
                values: new object[,]
                {
                    { "OptOutMessageBody", "PENDING LEGAL APPROVAL: You have been unsubscribed from Southern Company Gas text messages. Reply START or call 1-800-XXX-XXXX to re-subscribe.", "TCPA opt-out confirmation message body — MUST be approved by Legal before go-live" },
                    { "OptedInReportRecipients", "[]", "JSON array of email addresses for opted-in volume report" },
                    { "OptedOutReportRecipients", "[]", "JSON array of email addresses for opted-out volume report" },
                    { "ComplianceReportRecipients", "[]", "JSON array of email addresses for weekly compliance report — MUST be configured before go-live" },
                    { "ReportScheduleCron", "0 6 * * 1", "Cron expression for weekly report generation (Monday 06:00 Eastern)" },
                    { "AdminRateLimitPerMinute", "10", "Maximum admin re-opt-in requests per minute per API key" },
                    { "DebugLoggingEnabled", "false", "Set to 'true' to enable debug logging with unhashed phone numbers — requires access-controlled activation" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "OptOutMessageBody");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "OptedInReportRecipients");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "OptedOutReportRecipients");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "ComplianceReportRecipients");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "ReportScheduleCron");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "AdminRateLimitPerMinute");

            migrationBuilder.DeleteData(
                table: "SystemConfig",
                keyColumn: "Key",
                keyValue: "DebugLoggingEnabled");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SystemConfig",
                table: "SystemConfig");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "SystemConfig");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SystemConfig");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SystemConfig");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "SystemConfig");

            migrationBuilder.RenameTable(
                name: "SystemConfig",
                newName: "SystemConfigs");

            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "SystemConfigs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SystemConfigs",
                table: "SystemConfigs",
                column: "Id");
        }
    }
}
