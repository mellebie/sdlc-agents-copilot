using Microsoft.EntityFrameworkCore.Migrations;

namespace TCPA.Api.Infrastructure.Data.Migrations;

/// <summary>
/// Initial database schema migration for the TCPA Compliance API.
/// Creates all four core tables:
/// - ApplicationRegistrations: Application registry (SPEC-014)
/// - CellNumberOptOutRecords: Authoritative opt-out status store (SPEC-004, SPEC-006)
/// - AuditLogEntries: Immutable compliance audit log (SPEC-008, SPEC-009, SPEC-010)
/// - SmsMessageLogs: Operational message telemetry (SPEC-011, SPEC-012)
///
/// POST-MIGRATION STEPS (performed by platform team — NOT automated):
/// 1. Apply Azure SQL Always Encrypted to CellPhoneNumber columns on:
///    - CellNumberOptOutRecords.CellPhoneNumber
///    - AuditLogEntries.CellPhoneNumber
///    - SmsMessageLogs.CellPhoneNumber
///    Using deterministic AES-256 encryption with CMK in Azure Key Vault (TASK-061).
///
/// 2. Apply the audit log immutability DDL trigger (TASK-064):
///    CREATE TRIGGER trg_AuditLogEntries_Immutability
///    ON AuditLogEntries FOR UPDATE, DELETE
///    AS RAISERROR('AuditLogEntries records are immutable and cannot be modified.', 16, 1);
///    ROLLBACK;
///
/// 3. Seed the five initial application registrations (TASK-049).
/// </summary>
public partial class InitialSchema : Migration
{
    /// <inheritdoc/>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- ApplicationRegistrations ---
        migrationBuilder.CreateTable(
            name: "ApplicationRegistrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                CoolTextAccountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ApplicationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CallbackUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                OnboardedDate = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ApplicationRegistrations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ApplicationRegistrations_CoolTextAccountNumber",
            table: "ApplicationRegistrations",
            column: "CoolTextAccountNumber",
            unique: true);

        // --- CellNumberOptOutRecords ---
        // NOTE: CellPhoneNumber column requires Always Encrypted post-migration (see header).
        migrationBuilder.CreateTable(
            name: "CellNumberOptOutRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                CellPhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                LastOptOutTimestamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastOptInTimestamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CellNumberOptOutRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CellNumberOptOutRecords_CellPhoneNumber",
            table: "CellNumberOptOutRecords",
            column: "CellPhoneNumber",
            unique: true);

        // --- AuditLogEntries ---
        // NOTE: CellPhoneNumber column requires Always Encrypted post-migration (see header).
        // NOTE: Immutability DDL trigger must be applied post-migration (TASK-064).
        migrationBuilder.CreateTable(
            name: "AuditLogEntries",
            columns: table => new
            {
                RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                EventTimestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                CellPhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OriginatingCoolTextAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OriginatingApplicationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                OptOutKeywordReceived = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                MessageBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SystemResponse = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ConfirmationSmsSentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                ConfirmationSmsTimestamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                SuppressionReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                AgentUserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TicketReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                PreviousStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogEntries", x => x.RecordId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_EventTimestamp",
            table: "AuditLogEntries",
            column: "EventTimestamp");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_ApplicationName",
            table: "AuditLogEntries",
            column: "OriginatingApplicationName");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_EventType_EventTimestamp",
            table: "AuditLogEntries",
            columns: new[] { "EventType", "EventTimestamp" });

        // --- SmsMessageLogs ---
        // NOTE: CellPhoneNumber column requires Always Encrypted post-migration (see header).
        migrationBuilder.CreateTable(
            name: "SmsMessageLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                CellPhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ApplicationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Direction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                MessageContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SmsMessageLogs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SmsMessageLogs_Timestamp",
            table: "SmsMessageLogs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_SmsMessageLogs_ApplicationName",
            table: "SmsMessageLogs",
            column: "ApplicationName");
    }

    /// <inheritdoc/>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SmsMessageLogs");
        migrationBuilder.DropTable(name: "AuditLogEntries");
        migrationBuilder.DropTable(name: "CellNumberOptOutRecords");
        migrationBuilder.DropTable(name: "ApplicationRegistrations");
    }
}
