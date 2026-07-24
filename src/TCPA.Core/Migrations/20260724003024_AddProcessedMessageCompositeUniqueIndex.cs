using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessageCompositeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_MessageId_Endpoint",
                table: "ProcessedMessages",
                columns: new[] { "MessageId", "Endpoint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedMessages_MessageId_Endpoint",
                table: "ProcessedMessages");
        }
    }
}
