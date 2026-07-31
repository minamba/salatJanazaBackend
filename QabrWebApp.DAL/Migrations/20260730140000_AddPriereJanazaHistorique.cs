using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QabrWebApp.Dal.Entities;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    [DbContext(typeof(QabrWebAppDatabaseContext))]
    [Migration("20260730140000_AddPriereJanazaHistorique")]
    public partial class AddPriereJanazaHistorique : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrieresJanazaHistorique",
                columns: table => new
                {
                    Id              = table.Column<int>(nullable: false)
                                          .Annotation("SqlServer:Identity", "1, 1"),
                    DateCreation    = table.Column<DateTime>(nullable: false),
                    Genre           = table.Column<string>(type: "nvarchar(10)",  maxLength: 10,  nullable: true),
                    NomDefunt       = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EstAnonyme      = table.Column<bool>(nullable: false, defaultValue: false),
                    DeclarantPrenom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeclarantNom    = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MosqueeNom      = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Pays            = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_PrieresJanazaHistorique", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_PrieresJanazaHistorique_DateCreation",
                table: "PrieresJanazaHistorique",
                column: "DateCreation");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrieresJanazaHistorique");
        }
    }
}
