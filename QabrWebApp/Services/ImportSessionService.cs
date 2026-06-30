using System.Collections.Concurrent;

namespace QabrWebApp.Services
{
    public class ImportSessionService : IImportSessionService
    {
        private readonly ConcurrentDictionary<string, ImportSession> _sessions = new();

        public ImportSession Create(int utilisateurId, string? expoPushToken)
        {
            CleanupExpired();
            var session = new ImportSession
            {
                Token = Guid.NewGuid().ToString("N"),
                UtilisateurId = utilisateurId,
                ExpoPushToken = expoPushToken,
            };
            _sessions[session.Token] = session;
            return session;
        }

        public ImportSession? Get(string token)
        {
            if (!_sessions.TryGetValue(token, out var s)) return null;
            if (s.Status == ImportStatus.Pending && DateTime.UtcNow - s.CreatedAt > TimeSpan.FromMinutes(3))
            {
                s.Status = ImportStatus.Error;
                s.Message = "L'agent IA n'a pas répondu dans le délai imparti (3 min). Vérifiez que l'agent est actif et surveille le bon dossier.";
            }
            return s;
        }

        public void SetSuccess(string token, int priereId, bool timeUnknown = false)
        {
            if (_sessions.TryGetValue(token, out var s))
            {
                s.Status = ImportStatus.Success;
                s.PriereId = priereId;
                s.TimeUnknown = timeUnknown;
                s.Message = "La janaza a été importée avec succès.";
            }
        }

        public void SetError(string token, string message, string? errorCode = null)
        {
            if (_sessions.TryGetValue(token, out var s))
            {
                s.Status = ImportStatus.Error;
                s.Message = message;
                s.ErrorCode = errorCode;
            }
        }

        private void CleanupExpired()
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            foreach (var key in _sessions.Keys.ToList())
            {
                if (_sessions.TryGetValue(key, out var s) && s.CreatedAt < cutoff)
                    _sessions.TryRemove(key, out _);
            }
        }
    }
}
