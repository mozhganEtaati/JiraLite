using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraLite.Api.Common.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkTrackingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Issue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ParentIssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BoardColumnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Rank = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueDateUtc = table.Column<DateOnly>(type: "date", nullable: true),
                    Estimate = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issue_BoardColumn_BoardColumnId",
                        column: x => x.BoardColumnId,
                        principalTable: "BoardColumn",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_Issue_ParentIssueId",
                        column: x => x.ParentIssueId,
                        principalTable: "Issue",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_Sprint_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprint",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_User_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_User_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_User_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Issue_User_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Label",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Label", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Label_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachment_Issue_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attachment_User_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Issue_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comment_User_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IssueLabel",
                columns: table => new
                {
                    IssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLabel", x => new { x.IssueId, x.LabelId });
                    table.ForeignKey(
                        name: "FK_IssueLabel_Issue_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueLabel_Label_LabelId",
                        column: x => x.LabelId,
                        principalTable: "Label",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_IssueId",
                table: "Attachment",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_UploadedByUserId",
                table: "Attachment",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_AuthorUserId",
                table: "Comment",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_IssueId_CreatedAtUtc",
                table: "Comment",
                columns: new[] { "IssueId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Issue_AssigneeUserId",
                table: "Issue",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_BoardColumnId",
                table: "Issue",
                column: "BoardColumnId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_CreatedByUserId",
                table: "Issue",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ParentIssueId",
                table: "Issue",
                column: "ParentIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ProjectId_AssigneeUserId",
                table: "Issue",
                columns: new[] { "ProjectId", "AssigneeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ProjectId_BoardColumnId",
                table: "Issue",
                columns: new[] { "ProjectId", "BoardColumnId" });

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ProjectId_DueDateUtc",
                table: "Issue",
                columns: new[] { "ProjectId", "DueDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ProjectId_Number",
                table: "Issue",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ProjectId_SprintId_Rank",
                table: "Issue",
                columns: new[] { "ProjectId", "SprintId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_Issue_ReporterUserId",
                table: "Issue",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_SprintId",
                table: "Issue",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Issue_UpdatedByUserId",
                table: "Issue",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLabel_LabelId",
                table: "IssueLabel",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_Label_ProjectId_Name",
                table: "Label",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachment");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "IssueLabel");

            migrationBuilder.DropTable(
                name: "Issue");

            migrationBuilder.DropTable(
                name: "Label");
        }
    }
}
