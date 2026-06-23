using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;

namespace QabrWebApp.Builders
{
    public interface IPriereJanazaViewModelBuilder
    {
        PriereJanazaViewModel Build(PriereJanaza priere);
        List<PriereJanazaViewModel> BuildList(List<PriereJanaza> prieres);
    }
}
