using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders.impl
{
    public class UtilisateurViewModelBuilder : IUtilisateurViewModelBuilder
    {
        public UtilisateurViewModel Build(Utilisateur utilisateur) => new()
        {
            Id = utilisateur.Id,
            IdentityUserId = utilisateur.IdentityUserId,
            Prenom = utilisateur.Prenom,
            Nom = utilisateur.Nom,
            Email = utilisateur.Email,
            Telephone = utilisateur.Telephone,
            AdresseDomicile = utilisateur.AdresseDomicile,
            LatitudeDomicile = utilisateur.LatitudeDomicile,
            LongitudeDomicile = utilisateur.LongitudeDomicile,
            RayonNotification = utilisateur.RayonNotification,
            NotifMouvement = utilisateur.NotifMouvement,
            DateInscription = utilisateur.DateInscription,
            Role = utilisateur.Role,
            Language = utilisateur.Language,
        };

        public List<UtilisateurViewModel> BuildList(List<Utilisateur> utilisateurs)
            => utilisateurs.Select(Build).ToList();
    }
}
