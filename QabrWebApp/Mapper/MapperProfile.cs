using AutoMapper;
using QabrWebApp.Domain.Models;
using QabrWebApp.ViewModels;
using QabrWebApp.Request;

namespace QabrWebApp.Mapper
{
    public class MapperProfile : Profile
    {
        public MapperProfile()
        {
            CreateMap<Mosquee, MosqueeViewModel>().ReverseMap();
            CreateMap<PriereJanaza, PriereJanazaViewModel>()
                .ForMember(d => d.MosqueeNom, o => o.MapFrom(s => s.Mosquee != null ? s.Mosquee.Nom : null))
                .ForMember(d => d.MosqueeAdresse, o => o.MapFrom(s => s.Mosquee != null ? s.Mosquee.Adresse : null))
                .ForMember(d => d.Statut, o => o.MapFrom(s => s.Statut.ToString()));
            CreateMap<Utilisateur, UtilisateurViewModel>().ReverseMap();
            CreateMap<Abonnement, AbonnementViewModel>()
                .ForMember(d => d.MosqueeNom, o => o.MapFrom(s => s.Mosquee != null ? s.Mosquee.Nom : null))
                .ForMember(d => d.MosqueeAdresse, o => o.MapFrom(s => s.Mosquee != null ? s.Mosquee.Adresse : null))
                .ForMember(d => d.MosqueeLatitude, o => o.MapFrom(s => s.Mosquee != null ? (double?)s.Mosquee.Latitude : null))
                .ForMember(d => d.MosqueeLongitude, o => o.MapFrom(s => s.Mosquee != null ? (double?)s.Mosquee.Longitude : null));

            CreateMap<MosqueeRequest, Mosquee>();
            CreateMap<PriereJanazaRequest, PriereJanaza>();
            CreateMap<UtilisateurRequest, Utilisateur>();
            CreateMap<AbonnementRequest, Abonnement>();
        }
    }
}
