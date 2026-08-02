using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraLite.Api.Common.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignNotificationIndexWithSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notification_RecipientUserId_CreatedAtUtc",
                table: "Notification");

            migrationBuilder.DropIndex(
                name: "IX_Notification_RecipientUserId_IsRead",
                table: "Notification");

            migrationBuilder.CreateIndex(
                name: "IX_Notification_RecipientUserId_IsRead_CreatedAtUtc",
                table: "Notification",
                columns: new[] { "RecipientUserId", "IsRead", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notification_RecipientUserId_IsRead_CreatedAtUtc",
                table: "Notification");

            migrationBuilder.CreateIndex(
                name: "IX_Notification_RecipientUserId_CreatedAtUtc",
                table: "Notification",
                columns: new[] { "RecipientUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_RecipientUserId_IsRead",
                table: "Notification",
                columns: new[] { "RecipientUserId", "IsRead" });
        }
    }
}
