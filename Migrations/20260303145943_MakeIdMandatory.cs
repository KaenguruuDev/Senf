using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Senf.Migrations
{
    /// <inheritdoc />
    public partial class MakeIdMandatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "LastUpdatedBySshKeyId",
                table: "EnvFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "LastUpdatedBySshKeyId",
                table: "EnvFiles",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
