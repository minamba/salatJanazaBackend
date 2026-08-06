using Microsoft.EntityFrameworkCore;
using QabrWebApp.Dal.Entities;

namespace QabrWebApp.Services
{
    /// <summary>Compte rendu d'un passage de rattrapage.</summary>
    public record RattrapageLieux(int ACompleter, int Resolues, int Introuvables, int Restantes);

    public interface IRattrapageLieuxService
    {
        Task<RattrapageLieux> RemplirAsync(int maximum, CancellationToken ct = default);
        Task<(int total, int sansVille)> EtatAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Remplit `Ville` et `Pays` des mosquées qui ne les ont pas encore.
    ///
    /// POURQUOI CE PASSAGE EXISTE
    /// --------------------------
    /// Les deux colonnes sont arrivées par migration, donc vides sur toutes les
    /// mosquées déjà en base. Le remplissage à la création ne concerne que les
    /// nouvelles : sans ce rattrapage, les filtres de l'administration
    /// resteraient vides jusqu'à ce que chaque mosquée soit recréée à la main.
    ///
    /// POURQUOI IL EST DÉCLENCHÉ ET NON AUTOMATIQUE
    /// --------------------------------------------
    /// Nominatim impose une requête par seconde : quelques centaines de
    /// mosquées font plusieurs minutes de trafic sortant. Le lancer à chaque
    /// démarrage de l'API solliciterait un service tiers gratuit pour refaire
    /// un travail déjà fait.
    ///
    /// IDEMPOTENT, ET REPRENABLE
    /// -------------------------
    /// Seules les lignes sans ville sont traitées. Un passage interrompu se
    /// relance sans dégât et reprend là où il s'était arrêté. `maximum` borne
    /// le lot pour qu'un appel HTTP ne dépasse pas le délai d'attente du
    /// client — l'administration rappelle tant qu'il reste des lignes.
    /// </summary>
    public class RattrapageLieuxService : IRattrapageLieuxService
    {
        private readonly QabrWebAppDatabaseContext _db;
        private readonly IGeocodageInverseService _geocodage;
        private readonly ILogger<RattrapageLieuxService> _logger;

        public RattrapageLieuxService(
            QabrWebAppDatabaseContext db,
            IGeocodageInverseService geocodage,
            ILogger<RattrapageLieuxService> logger)
        {
            _db = db;
            _geocodage = geocodage;
            _logger = logger;
        }

        public async Task<(int total, int sansVille)> EtatAsync(CancellationToken ct = default)
        {
            var total = await _db.Mosquees.CountAsync(ct);
            var sansVille = await _db.Mosquees.CountAsync(m => m.Ville == null, ct);
            return (total, sansVille);
        }

        public async Task<RattrapageLieux> RemplirAsync(int maximum, CancellationToken ct = default)
        {
            var aCompleter = await _db.Mosquees.CountAsync(m => m.Ville == null, ct);

            var lot = await _db.Mosquees
                .Where(m => m.Ville == null)
                .OrderBy(m => m.Id)
                .Take(maximum)
                .ToListAsync(ct);

            int resolues = 0, introuvables = 0;

            foreach (var mosquee in lot)
            {
                ct.ThrowIfCancellationRequested();

                var lieu = await _geocodage.ResoudreAsync(mosquee.Latitude, mosquee.Longitude, ct);

                if (lieu?.Ville is null)
                {
                    // On ne marque rien : la ligne restera candidate au
                    // prochain passage. Une panne réseau ne doit pas condamner
                    // définitivement une mosquée à n'avoir aucune ville.
                    introuvables++;
                    continue;
                }

                mosquee.Ville = lieu.Ville;
                mosquee.Pays = lieu.Pays;
                resolues++;

                // Sauvegarde par paquets de vingt : assez rare pour ne pas
                // peser, assez fréquent pour qu'une interruption au bout de
                // cinq minutes ne jette pas cinq minutes de travail.
                if (resolues % 20 == 0) await _db.SaveChangesAsync(ct);
            }

            await _db.SaveChangesAsync(ct);

            var restantes = await _db.Mosquees.CountAsync(m => m.Ville == null, ct);

            _logger.LogInformation(
                "[Rattrapage] {Resolues} résolue(s), {Introuvables} introuvable(s), {Restantes} restante(s) sur {ACompleter}",
                resolues, introuvables, restantes, aCompleter);

            return new RattrapageLieux(aCompleter, resolues, introuvables, restantes);
        }
    }
}
