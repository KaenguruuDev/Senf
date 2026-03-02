using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Senf.Migrations
{
    /// <inheritdoc />
    public partial class UpdateEnvFileTrackingSshKeyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastUpdatedBySshKeyId",
                table: "EnvFiles",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastUpdatedBySshKeyId",
                table: "EnvFiles");
        }
    }
}
