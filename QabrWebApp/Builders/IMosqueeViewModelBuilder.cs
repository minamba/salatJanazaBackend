using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders
{
    public interface IMosqueeViewModelBuilder
    {
        MosqueeViewModel Build(Mosquee mosquee, double? distanceKm = null);
        List<MosqueeViewModel> BuildList(List<Mosquee> mosquees);
    }
}
