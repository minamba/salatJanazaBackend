using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddGpsModeToUtilisateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LatitudeCourante",
                table: "Utilisateurs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LongitudeCourante",
                table: "Utilisateurs",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeLocalisation",
                table: "Utilisateurs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LatitudeCourante",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "LongitudeCourante",
                table: "Utilisateurs");

            migrationBuilder.DropColumn(
                name: "ModeLocalisation",
                table: "Utilisateurs");
        }
    }
}
