namespace QabrWebApp.Services
{
    public class LocalFlyerStorageService : IFlyerStorageService
    {
        private readonly string _storagePath;
        private readonly string _baseUrl;

        public LocalFlyerStorageService(IConfiguration config, IWebHostEnvironment env)
        {
            // Dossier de stockage : wwwroot/flyers/ par défaut, configurable via Flyers:StoragePath
            _storagePath = config["Flyers:StoragePath"]
                ?? Path.Combine(env.WebRootPath ?? env.ContentRootPath, "flyers");

            // URL de base : l'URL publique du serveur, configurable via Flyers:BaseUrl
            _baseUrl = (config["Flyers:BaseUrl"] ?? config["AppBaseUrl"] ?? "http://localhost:5168").TrimEnd('/');

            Directory.CreateDirectory(_storagePath);
        }

        public async Task<string> SaveAsync(Stream fileStream, string fileName, string contentType)
        {
            var filePath = Path.Combine(_storagePath, fileName);
            await using var fs = System.IO.File.Create(filePath);
            await fileStream.CopyToAsync(fs);
            return $"{_baseUrl}/flyers/{fileName}";
        }
    }
}
