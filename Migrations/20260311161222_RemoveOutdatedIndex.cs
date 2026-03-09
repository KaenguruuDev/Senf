using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Senf.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOutdatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shares_Id",
                table: "Shares");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Shares_Id",
                table: "Shares",
                column: "Id",
                unique: true);
        }
    }
}
