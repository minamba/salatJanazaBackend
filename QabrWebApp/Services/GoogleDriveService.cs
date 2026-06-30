using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace QabrWebApp.Services
{
    public class GoogleDriveService : IGoogleDriveService
    {
        private readonly DriveService _drive;
        private readonly string _folderId;

        public GoogleDriveService(IConfiguration config)
        {
            _folderId = config["GoogleDrive:FolderId"] ?? throw new InvalidOperationException("GoogleDrive:FolderId manquant.");

            var section = config.GetSection("GoogleDrive:ServiceAccount");
            var saJson = System.Text.Json.JsonSerializer.Serialize(
                section.GetChildren().ToDictionary(c => c.Key, c => (object?)(c.Value ?? "")));

            var credential = GoogleCredential.FromJson(saJson)
                .CreateScoped(DriveService.Scope.Drive);

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "QabrApp"
            });
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        {
            var fileMeta = new Google.Apis.Drive.v3.Data.File
            {
                Name = fileName,
                Parents = new List<string> { _folderId }
            };

            var request = _drive.Files.Create(fileMeta, fileStream, contentType);
            request.Fields = "id";
            var progress = await request.UploadAsync();

            if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
                throw new Exception($"Upload Drive échoué : {progress.Exception?.Message ?? "erreur inconnue"}", progress.Exception);

            return request.ResponseBody?.Id ?? throw new Exception("Upload Drive échoué : ID de fichier manquant.");
        }
    }
}
