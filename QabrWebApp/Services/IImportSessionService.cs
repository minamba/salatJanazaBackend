namespace QabrWebApp.Services
{
    public enum ImportStatus { Pending, Success, Error }

    public class ImportSession
    {
        public string Token { get; set; } = string.Empty;
        public int UtilisateurId { get; set; }
        public string? ExpoPushToken { get; set; }
        public ImportStatus Status { get; set; } = ImportStatus.Pending;
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
        public int? PriereId { get; set; }
        public bool TimeUnknown { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public interface IImportSessionService
    {
        ImportSession Create(int utilisateurId, string? expoPushToken);
        ImportSession? Get(string token);
        void SetSuccess(string token, int priereId, bool timeUnknown = false);
        void SetError(string token, string message, string? errorCode = null);
    }
}
