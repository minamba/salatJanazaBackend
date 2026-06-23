namespace QabrWebApp.Dal.Entities
{
    public class UtilisateurToken
    {
        public int Id { get; set; }
        public int UtilisateurId { get; set; }
        public string ExpoToken { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public Utilisateur? Utilisateur { get; set; }
    }
}
