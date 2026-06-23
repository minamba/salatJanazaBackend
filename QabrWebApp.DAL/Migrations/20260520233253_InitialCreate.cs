using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mosquees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Adresse = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    OsmId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DateCreation = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mosquees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Utilisateurs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdentityUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Prenom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Telephone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ExpoToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdresseDomicile = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LatitudeDomicile = table.Column<double>(type: "float", nullable: true),
                    LongitudeDomicile = table.Column<double>(type: "float", nullable: true),
                    RayonNotification = table.Column<int>(type: "int", nullable: false),
                    NotifMouvement = table.Column<bool>(type: "bit", nullable: false),
                    DateInscription = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilisateurs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Abonnements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UtilisateurId = table.Column<int>(type: "int", nullable: false),
                    MosqueeId = table.Column<int>(type: "int", nullable: false),
                    NotifActive = table.Column<bool>(type: "bit", nullable: false),
                    DateAbonnement = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abonnements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abonnements_Mosquees_MosqueeId",
                        column: x => x.MosqueeId,
                        principalTable: "Mosquees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Abonnements_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrieresJanaza",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MosqueeId = table.Column<int>(type: "int", nullable: false),
                    UtilisateurId = table.Column<int>(type: "int", nullable: true),
                    NomDefunt = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EstAnonyme = table.Column<bool>(type: "bit", nullable: false),
                    DateHeurePriere = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Commentaire = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Statut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateCreation = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrieresJanaza", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrieresJanaza_Mosquees_MosqueeId",
                        column: x => x.MosqueeId,
                        principalTable: "Mosquees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrieresJanaza_Utilisateurs_UtilisateurId",
                        column: x => x.UtilisateurId,
                        principalTable: "Utilisateurs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Abonnements_MosqueeId",
                table: "Abonnements",
                column: "MosqueeId");

            migrationBuilder.CreateIndex(
                name: "IX_Abonnements_UtilisateurId_MosqueeId",
                table: "Abonnements",
                columns: new[] { "UtilisateurId", "MosqueeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mosquees_OsmId",
                table: "Mosquees",
                column: "OsmId",
                unique: true,
                filter: "[OsmId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrieresJanaza_MosqueeId",
                table: "PrieresJanaza",
                column: "MosqueeId");

            migrationBuilder.CreateIndex(
                name: "IX_PrieresJanaza_UtilisateurId",
                table: "PrieresJanaza",
                column: "UtilisateurId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Abonnements");

            migrationBuilder.DropTable(
                name: "PrieresJanaza");

            migrationBuilder.DropTable(
                name: "Mosquees");

            migrationBuilder.DropTable(
                name: "Utilisateurs");
        }
    }
}
