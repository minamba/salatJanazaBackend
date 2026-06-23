using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders
{
    public interface IUtilisateurViewModelBuilder
    {
        UtilisateurViewModel Build(Utilisateur utilisateur);
        List<UtilisateurViewModel> BuildList(List<Utilisateur> utilisateurs);
    }
}
