using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders.impl
{
    public class AbonnementViewModelBuilder : IAbonnementViewModelBuilder
    {
        public AbonnementViewModel Build(Abonnement abonnement) => new()
        {
            Id = abonnement.Id,
            UtilisateurId = abonnement.UtilisateurId,
            MosqueeId = abonnement.MosqueeId,
            MosqueeNom = abonnement.Mosquee?.Nom,
            MosqueeAdresse = abonnement.Mosquee?.Adresse,
            MosqueeLatitude = abonnement.Mosquee?.Latitude,
            MosqueeLongitude = abonnement.Mosquee?.Longitude,
            MosqueeOsmId = abonnement.Mosquee?.OsmId,
            NotifActive = abonnement.NotifActive,
            DateAbonnement = abonnement.DateAbonnement,
        };

        public List<AbonnementViewModel> BuildList(List<Abonnement> abonnements)
            => abonnements.Select(Build).ToList();
    }
}
