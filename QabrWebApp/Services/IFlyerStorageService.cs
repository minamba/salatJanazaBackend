namespace QabrWebApp.Services
{
    public interface IFlyerStorageService
    {
        /// <summary>Sauvegarde le fichier et retourne l'URL publique d'accès.</summary>
        Task<string> SaveAsync(Stream fileStream, string fileName, string contentType);
    }
}
