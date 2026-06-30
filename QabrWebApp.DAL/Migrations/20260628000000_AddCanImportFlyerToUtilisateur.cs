using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddCanImportFlyerToUtilisateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanImportFlyer",
                table: "Utilisateurs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanImportFlyer",
                table: "Utilisateurs");
        }
    }
}
