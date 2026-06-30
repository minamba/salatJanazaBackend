using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class UtilisateurRepository : IUtilisateurRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public UtilisateurRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task<List<DomainModel.Utilisateur>> GetAllAsync()
        {
            var entities = await _ctx.Utilisateurs.AsNoTracking().ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.Utilisateur?> GetByIdAsync(int id)
        {
            var e = await _ctx.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Utilisateur?> GetByIdentityIdAsync(string identityUserId)
        {
            var e = await _ctx.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.IdentityUserId == identityUserId);
            return e is null ? null : ToModel(e);
        }

        public async Task<DomainModel.Utilisateur> CreateAsync(DomainModel.Utilisateur utilisateur)
        {
            var entity = ToEntity(utilisateur);
            _ctx.Utilisateurs.Add(entity);
            await _ctx.SaveChangesAsync();
            utilisateur.Id = entity.Id;
            return utilisateur;
        }

        public async Task<DomainModel.Utilisateur> UpdateAsync(DomainModel.Utilisateur utilisateur)
        {
            var entity = await _ctx.Utilisateurs.FindAsync(utilisateur.Id)
                ?? throw new KeyNotFoundException($"Utilisateur {utilisateur.Id} introuvable");
            entity.Prenom = utilisateur.Prenom;
            entity.Nom = utilisateur.Nom;
            entity.Email = utilisateur.Email;
            entity.Telephone = utilisateur.Telephone;
            entity.ExpoToken = utilisateur.ExpoToken;
            entity.AdresseDomicile = utilisateur.AdresseDomicile;
            entity.LatitudeDomicile = utilisateur.LatitudeDomicile;
            entity.LongitudeDomicile = utilisateur.LongitudeDomicile;
            entity.RayonNotification = utilisateur.RayonNotification;
            entity.NotifMouvement = utilisateur.NotifMouvement;
            entity.Role = utilisateur.Role;
            entity.Language = utilisateur.Language;
            entity.CanImportFlyer = utilisateur.CanImportFlyer;
            await _ctx.SaveChangesAsync();
            return ToModel(entity);
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _ctx.Utilisateurs.FindAsync(id);
            if (entity is not null) { _ctx.Utilisateurs.Remove(entity); await _ctx.SaveChangesAsync(); }
        }

        private static DomainModel.Utilisateur ToModel(Utilisateur e) => new()
        {
            Id = e.Id, IdentityUserId = e.IdentityUserId,
            Prenom = e.Prenom, Nom = e.Nom, Email = e.Email,
            Telephone = e.Telephone, ExpoToken = e.ExpoToken,
            AdresseDomicile = e.AdresseDomicile,
            LatitudeDomicile = e.LatitudeDomicile, LongitudeDomicile = e.LongitudeDomicile,
            RayonNotification = e.RayonNotification, NotifMouvement = e.NotifMouvement,
            DateInscription = e.DateInscription, Role = e.Role, Language = e.Language,
            CanImportFlyer = e.CanImportFlyer,
        };

        private static Utilisateur ToEntity(DomainModel.Utilisateur u) => new()
        {
            Id = u.Id, IdentityUserId = u.IdentityUserId,
            Prenom = u.Prenom, Nom = u.Nom, Email = u.Email,
            Telephone = u.Telephone, ExpoToken = u.ExpoToken,
            AdresseDomicile = u.AdresseDomicile,
            LatitudeDomicile = u.LatitudeDomicile, LongitudeDomicile = u.LongitudeDomicile,
            RayonNotification = u.RayonNotification, NotifMouvement = u.NotifMouvement,
            DateInscription = u.DateInscription, Role = u.Role, Language = u.Language,
        };
    }
}
