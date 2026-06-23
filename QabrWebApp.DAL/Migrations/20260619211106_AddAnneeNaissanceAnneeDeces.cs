using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddAnneeNaissanceAnneeDeces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnneeDeces",
                table: "PrieresJanaza",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnneeNaissance",
                table: "PrieresJanaza",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnneeDeces",
                table: "PrieresJanaza");

            migrationBuilder.DropColumn(
                name: "AnneeNaissance",
                table: "PrieresJanaza");
        }
    }
}
