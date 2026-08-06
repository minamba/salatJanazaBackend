using System.Collections.Concurrent;

namespace QabrWebApp.Services
{
    /// <summary>L'état d'un rattrapage, tel que l'administration le consulte.</summary>
    public record EtatRattrapage(
        bool EnCours,
        int Traitees,
        int Introuvables,
        int Restantes,
        int Total,
        string? Erreur);

    public interface IRattrapageLieuxWorker
    {
        /// <summary>Lance le rattrapage s'il ne tourne pas déjà. Rend true s'il a été démarré.</summary>
        bool Demarrer();
        Task<EtatRattrapage> EtatAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Fait tourner le rattrapage des lieux EN FOND, et non pendant la requête.
    ///
    /// POURQUOI CE N'EST PAS UN SIMPLE ENDPOINT
    /// ----------------------------------------
    /// Le géocodage est cadencé à une requête par seconde : sept cents mosquées
    /// demandent une douzaine de minutes. La version précédente faisait boucler
    /// le téléphone sur des appels successifs pendant tout ce temps — l'écran
    /// devait rester allumé, l'application au premier plan, et le moindre
    /// passage en arrière-plan ou verrouillage cassait la chaîne.
    ///
    /// Ici, l'administration donne le départ et repart aussitôt. Le serveur
    /// travaille seul ; l'écran interroge l'avancement quand il veut, et peut
    /// être fermé sans rien interrompre.
    ///
    /// UN SEUL RATTRAPAGE À LA FOIS
    /// ----------------------------
    /// Deux passages simultanés doubleraient la cadence vers Nominatim et
    /// feraient bannir l'adresse IP du serveur — ce qui casserait aussi la
    /// déclaration de prière, qui dépend du même service. D'où le drapeau
    /// atomique : un second appui pendant le traitement ne relance rien.
    /// </summary>
    public class RattrapageLieuxWorker : BackgroundService, IRattrapageLieuxWorker
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RattrapageLieuxWorker> _logger;

        // 0 = au repos, 1 = en cours. Interlocked plutôt qu'un bool : deux
        // requêtes HTTP peuvent arriver sur deux fils au même instant.
        private int _enCours;
        private readonly ConcurrentQueue<byte> _demandes = new();

        private volatile int _traitees;
        private volatile int _introuvables;
        private volatile string? _erreur;

        public RattrapageLieuxWorker(IServiceScopeFactory scopeFactory, ILogger<RattrapageLieuxWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public bool Demarrer()
        {
            if (Interlocked.CompareExchange(ref _enCours, 1, 0) != 0) return false;

            _traitees = 0;
            _introuvables = 0;
            _erreur = null;
            _demandes.Enqueue(1);
            return true;
        }

        public async Task<EtatRattrapage> EtatAsync(CancellationToken ct = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRattrapageLieuxService>();
            var (total, sansVille) = await service.EtatAsync(ct);

            return new EtatRattrapage(
                EnCours: Volatile.Read(ref _enCours) == 1,
                Traitees: _traitees,
                Introuvables: _introuvables,
                Restantes: sansVille,
                Total: total,
                Erreur: _erreur);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("RattrapageLieuxWorker démarré — en attente d'une demande");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_demandes.TryDequeue(out _))
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                try
                {
                    await TraiterAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _erreur = ex.Message;
                    _logger.LogError(ex, "[Rattrapage] Interrompu par une erreur");
                }
                finally
                {
                    Interlocked.Exchange(ref _enCours, 0);
                }
            }
        }

        private async Task TraiterAsync(CancellationToken ct)
        {
            _logger.LogInformation("[Rattrapage] Démarrage");

            // UN LOT STÉRILE N'EST PAS UNE RAISON D'ABANDONNER
            // -------------------------------------------------
            // Une version précédente s'arrêtait dès qu'un lot ne résolvait
            // rien. C'était confondre deux choses très différentes : des
            // coordonnées que le service ne reconnaîtra jamais, et un
            // incident passager — une coupure réseau, une limitation
            // momentanée de Nominatim. Au moindre hoquet, le traitement
            // s'arrêtait de lui-même en laissant des centaines de mosquées
            // derrière, sans que rien ne l'explique.
            //
            // On tolère donc trois lots stériles d'affilée, avec une pause
            // entre chacun pour laisser passer l'incident. Il faut que le
            // service reste muet sur cent vingt mosquées de suite pour qu'on
            // renonce — ce qui ne peut plus être un hasard.
            const int lotsSterilesTolerees = 3;
            var steriles = 0;

            // Par lots, avec une portée neuve à chaque tour : garder un
            // DbContext ouvert un quart d'heure accumulerait tout le suivi de
            // modifications et finirait par peser lourd.
            for (;;)
            {
                ct.ThrowIfCancellationRequested();

                RattrapageLieux lot;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<IRattrapageLieuxService>();
                    lot = await service.RemplirAsync(40, ct);
                }

                _traitees += lot.Resolues;
                _introuvables += lot.Introuvables;

                _logger.LogInformation(
                    "[Rattrapage] {Traitees} localisée(s), {Introuvables} sans résultat, {Restantes} restante(s)",
                    _traitees, _introuvables, lot.Restantes);

                if (lot.Restantes == 0) break;

                if (lot.Resolues > 0)
                {
                    steriles = 0;
                    continue;
                }

                if (++steriles >= lotsSterilesTolerees)
                {
                    _logger.LogWarning(
                        "[Rattrapage] Arrêt : {Lots} lots consécutifs sans résultat, {Restantes} mosquée(s) toujours sans ville",
                        steriles, lot.Restantes);
                    break;
                }

                _logger.LogInformation(
                    "[Rattrapage] Lot sans résultat ({Steriles}/{Max}) — nouvelle tentative dans 10 s",
                    steriles, lotsSterilesTolerees);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }

            _logger.LogInformation("[Rattrapage] Terminé : {Traitees} localisée(s)", _traitees);
        }
    }
}
