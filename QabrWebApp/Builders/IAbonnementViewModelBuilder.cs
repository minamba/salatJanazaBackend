using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders
{
    public interface IAbonnementViewModelBuilder
    {
        AbonnementViewModel Build(Abonnement abonnement);
        List<AbonnementViewModel> BuildList(List<Abonnement> abonnements);
    }
}
