using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProcessedMessage_CompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedMessages_MessageId_Endpoint",
                table: "ProcessedMessages");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages",
                columns: new[] { "MessageId", "Endpoint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProcessedMessages",
                table: "ProcessedMessages",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_MessageId_Endpoint",
                table: "ProcessedMessages",
                columns: new[] { "MessageId", "Endpoint" },
                unique: true);
        }
    }
}
