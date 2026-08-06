using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders.impl
{
    public class MosqueeViewModelBuilder : IMosqueeViewModelBuilder
    {
        public MosqueeViewModel Build(Mosquee mosquee, double? distanceKm = null) => new()
        {
            Id = mosquee.Id,
            Nom = mosquee.Nom,
            Adresse = mosquee.Adresse,
            Ville = mosquee.Ville,
            Pays = mosquee.Pays,
            Latitude = mosquee.Latitude,
            Longitude = mosquee.Longitude,
            OsmId = mosquee.OsmId,
            Statut = mosquee.Statut,
            Source = mosquee.Source,
            DateCreation = mosquee.DateCreation,
            DistanceKm = distanceKm,
        };

        public List<MosqueeViewModel> BuildList(List<Mosquee> mosquees)
            => mosquees.Select(m => Build(m)).ToList();
    }
}
