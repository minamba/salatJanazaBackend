namespace QabrWebApp.Domain.Models
{
    public enum StatutPriere { AVenir, EnCours, Terminee, EnAttente, Brouillon }

    public class PriereJanaza
    {
        public int Id { get; set; }
        public int MosqueeId { get; set; }
        public int? UtilisateurId { get; set; }
        public string? NomDefunt { get; set; }
        public bool EstAnonyme { get; set; }
        public DateTime DateHeurePriere { get; set; }
        public string? Genre { get; set; }
        public string? Commentaire { get; set; }
        public string? PaysEnterrement { get; set; }
        public string? VilleEnterrement { get; set; }
        public int? AnneeNaissance { get; set; }
        public int? AnneeDeces { get; set; }
        public int UtcOffsetMinutes { get; set; }
        public StatutPriere Statut { get; set; }
        public DateTime DateCreation { get; set; }
        public Mosquee? Mosquee { get; set; }
    }
}
