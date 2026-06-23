using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddBurialFieldsToPriereJanaza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaysEnterrement",
                table: "PrieresJanaza",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VilleEnterrement",
                table: "PrieresJanaza",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaysEnterrement",
                table: "PrieresJanaza");

            migrationBuilder.DropColumn(
                name: "VilleEnterrement",
                table: "PrieresJanaza");
        }
    }
}
