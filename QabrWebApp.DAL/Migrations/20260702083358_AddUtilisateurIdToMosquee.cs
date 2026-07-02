using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddUtilisateurIdToMosquee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UtilisateurId",
                table: "Mosquees",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtilisateurId",
                table: "Mosquees");
        }
    }
}
