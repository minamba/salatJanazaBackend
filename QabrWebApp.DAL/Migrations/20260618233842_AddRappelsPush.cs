using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class AddRappelsPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RappelsPush",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MosqueeId = table.Column<int>(type: "int", nullable: false),
                    PriereJanazaId = table.Column<int>(type: "int", nullable: false),
                    DateEnvoi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnvoyeAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RappelsPush", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RappelsPush_PrieresJanaza_PriereJanazaId",
                        column: x => x.PriereJanazaId,
                        principalTable: "PrieresJanaza",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RappelsPush_DateEnvoi_EnvoyeAt",
                table: "RappelsPush",
                columns: new[] { "DateEnvoi", "EnvoyeAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RappelsPush_PriereJanazaId",
                table: "RappelsPush",
                column: "PriereJanazaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RappelsPush");
        }
    }
}
