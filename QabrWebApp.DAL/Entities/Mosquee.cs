using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QabrWebApp.Dal.Entities
{
    [Table("Mosquees")]
    public class Mosquee
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Nom { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Adresse { get; set; }

        // Ville et pays ne sont PAS extraits de l'adresse : ils viennent d'un
        // géocodage inverse sur les coordonnées ci-dessous, donc d'une donnée
        // structurée et non d'un texte libre. L'adresse, elle, est saisie à la
        // main ou recopiée d'OpenStreetMap, sous des formes inconciliables —
        // « Rue Morand, 75011 Paris » et « …, Évry, Essonne, 91000, France »
        // cohabitent dans la même colonne.
        //
        // Nullable à dessein : Nominatim ne connaît pas tous les points du
        // globe, et une mosquée sans ville doit rester visible plutôt que de
        // disparaître des listes.
        [MaxLength(120)]
        public string? Ville { get; set; }

        // Le nom du pays en clair (« France »), pas le code ISO : c'est ce que
        // l'administration affiche, et rien d'autre n'en dépend.
        [MaxLength(80)]
        public string? Pays { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [MaxLength(50)]
        public string? OsmId { get; set; }

        [MaxLength(20)]
        public string Statut { get; set; } = "Validee";

        [MaxLength(10)]
        public string Source { get; set; } = "user"; // "user" | "osm"

        public int? UtilisateurId { get; set; }

        public DateTime DateCreation { get; set; }

        public DateTime? DerniereSyncOsm { get; set; }

        public ICollection<PriereJanaza> PrieresJanaza { get; set; } = [];
        public ICollection<Abonnement> Abonnements { get; set; } = [];
    }
}
