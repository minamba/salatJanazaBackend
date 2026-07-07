namespace QabrWebApp.Services
{
    public interface ITextImportStorageService
    {
        /// <summary>Sauvegarde le texte brut en .txt sur Google Drive et retourne l'URL du fichier.</summary>
        Task<string> SaveAsync(string text, string fileName);
    }
}
