using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraLite.Api.Common.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueBlockedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CarriedForwardIssueCount",
                table: "Sprint",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "Issue",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedSinceUtc",
                table: "Issue",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Issue",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarriedForwardIssueCount",
                table: "Sprint");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "Issue");

            migrationBuilder.DropColumn(
                name: "BlockedSinceUtc",
                table: "Issue");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Issue");
        }
    }
}
