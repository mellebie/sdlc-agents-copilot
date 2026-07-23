using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateProcessedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ProcessedMessages");

            migrationBuilder.AddColumn<string>(
                name: "MessageId",
                table: "ProcessedMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "ProcessedMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "InternalId",
                table: "ProcessedMessages",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "ProcessedMessages",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ResponseStatus",
                table: "ProcessedMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_ProcessedAt",
                table: "ProcessedMessages",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedMessages_ProcessedAt",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "InternalId",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "ProcessedMessages");

            migrationBuilder.DropColumn(
                name: "ResponseStatus",
                table: "ProcessedMessages");

            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "ProcessedMessages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages",
                column: "Id");
        }
    }
}
