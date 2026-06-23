using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders.impl
{
    public class PriereJanazaViewModelBuilder : IPriereJanazaViewModelBuilder
    {
        public PriereJanazaViewModel Build(PriereJanaza priere) => new()
        {
            Id = priere.Id,
            MosqueeId = priere.MosqueeId,
            MosqueeNom = priere.Mosquee?.Nom,
            MosqueeAdresse = priere.Mosquee?.Adresse,
            UtilisateurId = priere.UtilisateurId,
            NomDefunt = priere.EstAnonyme ? null : priere.NomDefunt,
            EstAnonyme = priere.EstAnonyme,
            Genre = priere.Genre,
            DateHeurePriere = DateTime.SpecifyKind(priere.DateHeurePriere, DateTimeKind.Utc),
            Commentaire = priere.Commentaire,
            PaysEnterrement = priere.PaysEnterrement,
            VilleEnterrement = priere.VilleEnterrement,
            AnneeNaissance = priere.AnneeNaissance,
            AnneeDeces = priere.AnneeDeces,
            UtcOffsetMinutes = priere.UtcOffsetMinutes,
            Statut = priere.Statut.ToString(),
            DateCreation = DateTime.SpecifyKind(priere.DateCreation, DateTimeKind.Utc),
            MosqueeLatitude = priere.Mosquee?.Latitude,
            MosqueeLongitude = priere.Mosquee?.Longitude,
        };

        public List<PriereJanazaViewModel> BuildList(List<PriereJanaza> prieres)
            => prieres.Select(Build).ToList();
    }
}
