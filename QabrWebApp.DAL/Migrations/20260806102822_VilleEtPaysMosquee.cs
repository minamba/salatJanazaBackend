using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QabrWebApp.Dal.Migrations
{
    /// <summary>
    /// Ajoute `Ville` et `Pays` à Mosquees, renseignés par géocodage inverse
    /// des coordonnées.
    ///
    /// CETTE MIGRATION A ÉTÉ ÉLAGUÉE À LA MAIN — NE PAS LA RÉGÉNÉRER
    /// -------------------------------------------------------------
    /// EF l'avait produite avec deux objets de plus : la table
    /// `PrieresJanazaHistorique` et la colonne `Utilisateurs.Platform`. Tous
    /// deux EXISTENT DÉJÀ en base — ils y ont été créés sans passer par une
    /// migration, si bien qu'EF les croyait encore à faire. Les laisser aurait
    /// fait échouer le déploiement sur un « objet déjà existant », et laissé la
    /// base à moitié migrée.
    ///
    /// Ils restent présents dans le fichier .Designer et dans l'instantané du
    /// modèle, et c'est voulu : après cette migration, modèle, instantané et
    /// base décrivent enfin la même chose. C'est la mise à niveau habituelle
    /// d'objets créés hors migration.
    /// </summary>
    public partial class VilleEtPaysMosquee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ville",
                table: "Mosquees",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pays",
                table: "Mosquees",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Ville", table: "Mosquees");
            migrationBuilder.DropColumn(name: "Pays", table: "Mosquees");
        }
    }
}
