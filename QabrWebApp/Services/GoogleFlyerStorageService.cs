using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace QabrWebApp.Services
{
    public class GoogleFlyerStorageService : IFlyerStorageService
    {
        private readonly DriveService _drive;
        private readonly string _folderId;

        public GoogleFlyerStorageService(IConfiguration config)
        {
            _folderId = config["GoogleDrive:FolderId"]
                ?? throw new InvalidOperationException("GoogleDrive:FolderId manquant dans la config.");

            var clientId = config["GoogleDrive:ClientId"]
                ?? throw new InvalidOperationException("GoogleDrive:ClientId manquant dans la config.");
            var clientSecret = config["GoogleDrive:ClientSecret"]
                ?? throw new InvalidOperationException("GoogleDrive:ClientSecret manquant dans la config.");
            var refreshToken = config["GoogleDrive:RefreshToken"]
                ?? throw new InvalidOperationException("GoogleDrive:RefreshToken manquant dans la config. Voir docs/google-drive-setup.md.");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
                Scopes = new[] { DriveService.Scope.Drive }
            });

            var credential = new UserCredential(flow, "drive-uploader", new TokenResponse
            {
                RefreshToken = refreshToken
            });

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "QabrApp"
            });
        }

        public async Task<string> SaveAsync(Stream fileStream, string fileName, string contentType)
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
                throw new Exception(
                    $"Upload Google Drive échoué : {progress.Exception?.Message ?? "erreur inconnue"}",
                    progress.Exception);

            var fileId = request.ResponseBody?.Id
                ?? throw new Exception("Upload Google Drive échoué : ID de fichier manquant dans la réponse.");

            return $"https://drive.google.com/file/d/{fileId}/view";
        }
    }
}
