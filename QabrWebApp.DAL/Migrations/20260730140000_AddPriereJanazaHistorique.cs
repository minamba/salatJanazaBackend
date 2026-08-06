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
            // SQL brut plutôt que CreateTable, POUR ÊTRE REJOUABLE.
            //
            // Cette table est également créée en SQL idempotent par Program.cs
            // au démarrage — c'est comme cela qu'elle est apparue en production
            // avant l'existence de cette migration. Un CreateTable nu échouait
            // donc sur « objet déjà existant » et bloquait toute la chaîne des
            // migrations derrière lui.
            //
            // VilleEnterrement est incluse ici : la migration qui devait
            // l'ajouter est vide, précisément parce qu'elle ne pouvait pas
            // s'appliquer à une base vierge où la table n'existait pas encore.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.objects
                    WHERE object_id = OBJECT_ID(N'PrieresJanazaHistorique') AND type = N'U'
                )
                BEGIN
                    CREATE TABLE [PrieresJanazaHistorique] (
                        [Id]               INT IDENTITY(1,1)  NOT NULL,
                        [DateCreation]     DATETIME2(7)       NOT NULL,
                        [Genre]            NVARCHAR(10)       NULL,
                        [NomDefunt]        NVARCHAR(200)      NULL,
                        [EstAnonyme]       BIT                NOT NULL DEFAULT 0,
                        [DeclarantPrenom]  NVARCHAR(100)      NULL,
                        [DeclarantNom]     NVARCHAR(100)      NULL,
                        [MosqueeNom]       NVARCHAR(300)      NULL,
                        [Pays]             NVARCHAR(100)      NULL,
                        [VilleEnterrement] NVARCHAR(200)      NULL,
                        CONSTRAINT [PK_PrieresJanazaHistorique] PRIMARY KEY ([Id])
                    );

                    CREATE INDEX [IX_PrieresJanazaHistorique_DateCreation]
                        ON [PrieresJanazaHistorique] ([DateCreation]);
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PrieresJanazaHistorique");
        }
    }
}
