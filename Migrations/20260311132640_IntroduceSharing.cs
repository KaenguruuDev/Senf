using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Senf.Migrations
{
    /// <inheritdoc />
    public partial class IntroduceSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EnvFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    SharedToUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ShareMode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shares_EnvFiles_EnvFileId",
                        column: x => x.EnvFileId,
                        principalTable: "EnvFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Shares_Users_SharedToUserId",
                        column: x => x.SharedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shares_EnvFileId_SharedToUserId",
                table: "Shares",
                columns: new[] { "EnvFileId", "SharedToUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_Id",
                table: "Shares",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shares_SharedToUserId",
                table: "Shares",
                column: "SharedToUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Shares");
        }
    }
}
