using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Domain.Repositories;
using DomainModel = QabrWebApp.Domain.Models;
using EntityStatut = QabrWebApp.Dal.Entities.StatutPriereEntity;
using DomainStatut = QabrWebApp.Domain.Models.StatutPriere;

namespace QabrWebApp.Dal.Repositories
{
    public class PriereJanazaRepository : IPriereJanazaRepository
    {
        private readonly QabrWebAppDatabaseContext _ctx;

        public PriereJanazaRepository(QabrWebAppDatabaseContext ctx) => _ctx = ctx;

        public async Task<List<DomainModel.PriereJanaza>> GetAllAsync()
        {
            var entities = await _ctx.PrieresJanaza
                .Include(p => p.Mosquee)
                .AsNoTracking()
                .OrderByDescending(p => p.DateHeurePriere)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.PriereJanaza?> GetByIdAsync(int id)
        {
            var e = await _ctx.PrieresJanaza.Include(p => p.Mosquee).AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            return e is null ? null : ToModel(e);
        }

        public async Task<List<DomainModel.PriereJanaza>> GetByMosqueeIdAsync(int mosqueeId)
        {
            var entities = await _ctx.PrieresJanaza
                .Include(p => p.Mosquee)
                .AsNoTracking()
                .Where(p => p.MosqueeId == mosqueeId)
                .OrderByDescending(p => p.DateHeurePriere)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<List<DomainModel.PriereJanaza>> GetByUtilisateurIdAsync(int utilisateurId)
        {
            var entities = await _ctx.PrieresJanaza
                .Include(p => p.Mosquee)
                .AsNoTracking()
                .Where(p => p.UtilisateurId == utilisateurId)
                .OrderByDescending(p => p.DateHeurePriere)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<List<DomainModel.PriereJanaza>> GetUpcomingAsync()
        {
            var now = DateTime.UtcNow;
            var entities = await _ctx.PrieresJanaza
                .Include(p => p.Mosquee)
                .AsNoTracking()
                .Where(p => p.DateHeurePriere >= now || p.Statut == EntityStatut.EnCours)
                .OrderBy(p => p.DateHeurePriere)
                .ToListAsync();
            return entities.Select(ToModel).ToList();
        }

        public async Task<DomainModel.PriereJanaza> CreateAsync(DomainModel.PriereJanaza priere)
        {
            var entity = ToEntity(priere);
            _ctx.PrieresJanaza.Add(entity);
            await _ctx.SaveChangesAsync();
            var withMosquee = await _ctx.PrieresJanaza
                .Include(p => p.Mosquee)
                .AsNoTracking()
                .FirstAsync(p => p.Id == entity.Id);
            return ToModel(withMosquee);
        }

        public async Task<DomainModel.PriereJanaza> UpdateAsync(DomainModel.PriereJanaza priere)
        {
            var entity = await _ctx.PrieresJanaza.FindAsync(priere.Id)
                ?? throw new KeyNotFoundException($"PriereJanaza {priere.Id} introuvable");
            entity.NomDefunt = priere.NomDefunt;
            entity.EstAnonyme = priere.EstAnonyme;
            entity.Genre = priere.Genre;
            entity.DateHeurePriere = priere.DateHeurePriere;
            entity.Commentaire = priere.Commentaire;
            entity.PaysEnterrement = priere.PaysEnterrement;
            entity.VilleEnterrement = priere.VilleEnterrement;
            entity.AnneeNaissance = priere.AnneeNaissance;
            entity.AnneeDeces = priere.AnneeDeces;
            entity.UtcOffsetMinutes = priere.UtcOffsetMinutes;
            entity.Statut = ToEntityStatut(priere.Statut);
            await _ctx.SaveChangesAsync();
            return priere;
        }

        public async Task DeleteAsync(int id)
        {
            await _ctx.PrieresJanaza.Where(p => p.Id == id).ExecuteDeleteAsync();
        }

        private static DomainModel.PriereJanaza ToModel(PriereJanaza e) => new()
        {
            Id = e.Id, MosqueeId = e.MosqueeId, UtilisateurId = e.UtilisateurId,
            NomDefunt = e.NomDefunt, EstAnonyme = e.EstAnonyme, Genre = e.Genre,
            DateHeurePriere = e.DateHeurePriere, Commentaire = e.Commentaire,
            PaysEnterrement = e.PaysEnterrement, VilleEnterrement = e.VilleEnterrement,
            AnneeNaissance = e.AnneeNaissance, AnneeDeces = e.AnneeDeces,
            UtcOffsetMinutes = e.UtcOffsetMinutes,
            Statut = ToDomainStatut(e.Statut), DateCreation = e.DateCreation,
            Mosquee = e.Mosquee is null ? null : new DomainModel.Mosquee
            {
                Id = e.Mosquee.Id, Nom = e.Mosquee.Nom, Adresse = e.Mosquee.Adresse,
                Latitude = e.Mosquee.Latitude, Longitude = e.Mosquee.Longitude,
            },
        };

        private static PriereJanaza ToEntity(DomainModel.PriereJanaza p) => new()
        {
            Id = p.Id, MosqueeId = p.MosqueeId, UtilisateurId = p.UtilisateurId,
            NomDefunt = p.NomDefunt, EstAnonyme = p.EstAnonyme, Genre = p.Genre,
            DateHeurePriere = p.DateHeurePriere, Commentaire = p.Commentaire,
            PaysEnterrement = p.PaysEnterrement, VilleEnterrement = p.VilleEnterrement,
            AnneeNaissance = p.AnneeNaissance, AnneeDeces = p.AnneeDeces,
            UtcOffsetMinutes = p.UtcOffsetMinutes,
            Statut = ToEntityStatut(p.Statut), DateCreation = p.DateCreation,
        };

        private static DomainStatut ToDomainStatut(EntityStatut s) => s switch
        {
            EntityStatut.EnCours => DomainStatut.EnCours,
            EntityStatut.Terminee => DomainStatut.Terminee,
            _ => DomainStatut.AVenir,
        };

        private static EntityStatut ToEntityStatut(DomainStatut s) => s switch
        {
            DomainStatut.EnCours => EntityStatut.EnCours,
            DomainStatut.Terminee => EntityStatut.Terminee,
            _ => EntityStatut.AVenir,
        };
    }
}
