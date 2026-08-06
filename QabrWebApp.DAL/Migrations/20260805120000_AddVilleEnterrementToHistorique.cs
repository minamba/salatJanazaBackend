using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QabrWebApp.Dal.Entities;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    [DbContext(typeof(QabrWebAppDatabaseContext))]
    [Migration("20260805120000_AddVilleEnterrementToHistorique")]
    public partial class AddVilleEnterrementToHistorique : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PrieresJanazaHistorique est créée via SQL brut dans Program.cs APRÈS Migrate().
            // L'ALTER TABLE ici échouerait sur une base vierge (table absente).
            // La colonne VilleEnterrement est ajoutée par le bloc idempotent dans Program.cs.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
