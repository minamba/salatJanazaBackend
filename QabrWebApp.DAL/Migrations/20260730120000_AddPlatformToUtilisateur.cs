using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QabrWebApp.Dal.Entities;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    [DbContext(typeof(QabrWebAppDatabaseContext))]
    [Migration("20260730120000_AddPlatformToUtilisateur")]
    public partial class AddPlatformToUtilisateur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL brut plutôt que AddColumn, POUR ÊTRE REJOUABLE.
            //
            // Cette colonne a été ajoutée à la main en production avant que la
            // migration n'existe, et Program.cs la recrée aussi de façon
            // idempotente au démarrage. Un AddColumn nu échouait donc avec
            // « le nom de colonne Platform est spécifié plusieurs fois », ce
            // qui bloquait TOUTES les migrations suivantes — y compris celles
            // qui n'ont rien à voir.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'Utilisateurs') AND name = N'Platform'
                )
                ALTER TABLE [Utilisateurs] ADD [Platform] nvarchar(10) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Platform",
                table: "Utilisateurs");
        }
    }
}
