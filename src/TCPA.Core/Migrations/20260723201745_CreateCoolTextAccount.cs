using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TCPA.Core.Migrations
{
    /// <inheritdoc />
    public partial class CreateCoolTextAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CoolTextAccounts",
                table: "CoolTextAccounts");

            migrationBuilder.RenameTable(
                name: "CoolTextAccounts",
                newName: "CoolTextAccount");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "CoolTextAccount",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "CoolTextAccount",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationId",
                table: "CoolTextAccount",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "CoolTextAccount",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CallbackUrl",
                table: "CoolTextAccount",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "CoolTextAccount",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CoolTextAccount",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "CoolTextAccount",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoolTextAccount",
                table: "CoolTextAccount",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_CoolTextAccount_AccountNumber",
                table: "CoolTextAccount",
                column: "AccountNumber",
                unique: true);

            migrationBuilder.InsertData(
                table: "CoolTextAccount",
                columns: new[] { "AccountNumber", "ApplicationId", "ApplicationName", "CallbackUrl", "IsActive" },
                values: new object[,]
                {
                    // PENDING: Replace placeholder values with real Cool Text account numbers from IT team
                    { "CT-PLACEHOLDER-BIZTALK", "BizTalk", "BizTalk Integration Service", "https://PLACEHOLDER/biztalk/callback", true },
                    { "CT-PLACEHOLDER-GCMA", "GCMA", "Gas Customer Management Application", "https://PLACEHOLDER/gcma/callback", true },
                    { "CT-PLACEHOLDER-KMI", "KMI", "Key Management Interface", "https://PLACEHOLDER/kmi/callback", true },
                    { "CT-PLACEHOLDER-ARM", "ARM", "ARM / Construction Portal", "https://PLACEHOLDER/arm/callback", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "CoolTextAccount",
                keyColumn: "AccountNumber",
                keyValues: new object[]
                {
                    "CT-PLACEHOLDER-BIZTALK",
                    "CT-PLACEHOLDER-GCMA",
                    "CT-PLACEHOLDER-KMI",
                    "CT-PLACEHOLDER-ARM"
                });

            migrationBuilder.DropPrimaryKey(
                name: "PK_CoolTextAccount",
                table: "CoolTextAccount");

            migrationBuilder.DropIndex(
                name: "IX_CoolTextAccount_AccountNumber",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "ApplicationId",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "ApplicationName",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "CallbackUrl",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CoolTextAccount");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CoolTextAccount");

            migrationBuilder.RenameTable(
                name: "CoolTextAccount",
                newName: "CoolTextAccounts");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "CoolTextAccounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CoolTextAccounts",
                table: "CoolTextAccounts",
                column: "Id");
        }
    }
}
