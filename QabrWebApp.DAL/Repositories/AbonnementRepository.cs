using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;

namespace QabrWebApp.Dal.Repositories
{
    public class AbonnementRepository : IAbonnementRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public AbonnementRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task<List<DomainModel.Abonnement>> GetByUtilisateurIdAsync(int utilisateurId)
        {
            var entities = await _ctx.Abonnements
                .Include(a => a.Mosquee)
                .Include(a => a.Utilisateur)
                .AsNoTracking()
                .Where(a => a.UtilisateurId == utilisateurId)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<List<DomainModel.Abonnement>> GetByMosqueeIdAsync(int mosqueeId)
        {
            var entities = await _ctx.Abonnements
                .Include(a => a.Utilisateur)
                .AsNoTracking()
                .Where(a => a.MosqueeId == mosqueeId && a.NotifActive)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.Abonnement?> GetByIdAsync(int id)
        {
            var e = await _ctx.Abonnements
                .Include(a => a.Mosquee)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);
            return e is null ? null : ToModel(e);
        }

        public Task<bool> ExistsAsync(int utilisateurId, int mosqueeId)
            => _ctx.Abonnements.AnyAsync(a => a.UtilisateurId == utilisateurId && a.MosqueeId == mosqueeId);

        public async Task<DomainModel.Abonnement> CreateAsync(DomainModel.Abonnement abonnement)
        {
            var entity = ToEntity(abonnement);
            _ctx.Abonnements.Add(entity);
            await _ctx.SaveChangesAsync();
            abonnement.Id = entity.Id;
            return abonnement;
        }

        public async Task<DomainModel.Abonnement> UpdateAsync(DomainModel.Abonnement abonnement)
        {
            var entity = await _ctx.Abonnements.FindAsync(abonnement.Id)
                ?? throw new KeyNotFoundException($"Abonnement {abonnement.Id} introuvable");
            entity.NotifActive = abonnement.NotifActive;
            await _ctx.SaveChangesAsync();
            return abonnement;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _ctx.Abonnements.FindAsync(id);
            if (entity is not null) { _ctx.Abonnements.Remove(entity); await _ctx.SaveChangesAsync(); }
        }

        private static DomainModel.Abonnement ToModel(Abonnement e) => new()
        {
            Id = e.Id, UtilisateurId = e.UtilisateurId, MosqueeId = e.MosqueeId,
            NotifActive = e.NotifActive, DateAbonnement = e.DateAbonnement,
            Mosquee = e.Mosquee is null ? null : new DomainModel.Mosquee
            {
                Id = e.Mosquee.Id, Nom = e.Mosquee.Nom, Adresse = e.Mosquee.Adresse,
                Latitude = e.Mosquee.Latitude, Longitude = e.Mosquee.Longitude,
                OsmId = e.Mosquee.OsmId,
            },
            Utilisateur = e.Utilisateur is null ? null : new DomainModel.Utilisateur
            {
                Id = e.Utilisateur.Id, Email = e.Utilisateur.Email,
                Prenom = e.Utilisateur.Prenom, Nom = e.Utilisateur.Nom,
                ExpoToken = e.Utilisateur.ExpoToken,
            },
        };

        private static Abonnement ToEntity(DomainModel.Abonnement a) => new()
        {
            Id = a.Id, UtilisateurId = a.UtilisateurId, MosqueeId = a.MosqueeId,
            NotifActive = a.NotifActive, DateAbonnement = a.DateAbonnement,
        };
    }
}
