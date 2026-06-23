using System.Text.Json;

namespace QabrWebApp.IdentityServer.Services
{
    public record ExternalUserInfo(string ProviderKey, string Email, string? FirstName, string? LastName);

    public interface IExternalAuthService
    {
        Task<ExternalUserInfo?> ValidateGoogleTokenAsync(string accessToken);
        Task<ExternalUserInfo?> ValidateAppleTokenAsync(string identityToken);
    }

    public class ExternalAuthService : IExternalAuthService
    {
        private readonly HttpClient _http;

        public ExternalAuthService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("external");
        }

        public async Task<ExternalUserInfo?> ValidateGoogleTokenAsync(string accessToken)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var res = await _http.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadFromJsonAsync<JsonElement>();
                var email = json.TryGetProperty("email", out var e) ? e.GetString() : null;
                var sub = json.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(sub)) return null;

                var firstName = json.TryGetProperty("given_name", out var fn) ? fn.GetString() : null;
                var lastName = json.TryGetProperty("family_name", out var ln) ? ln.GetString() : null;

                return new ExternalUserInfo(sub, email, firstName, lastName);
            }
            catch
            {
                return null;
            }
        }

        public async Task<ExternalUserInfo?> ValidateAppleTokenAsync(string identityToken)
        {
            try
            {
                var parts = identityToken.Split('.');
                if (parts.Length != 3) return null;

                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload += new string('=', (4 - payload.Length % 4) % 4);
                var bytes = Convert.FromBase64String(payload);
                var json = JsonSerializer.Deserialize<JsonElement>(bytes);

                var sub = json.TryGetProperty("sub", out var s) ? s.GetString() : null;
                var email = json.TryGetProperty("email", out var em) ? em.GetString() : null;

                if (string.IsNullOrEmpty(sub)) return null;

                // Apple peut ne pas retourner l'email après la première connexion
                var finalEmail = email ?? $"{sub}@privaterelay.appleid.com";

                return new ExternalUserInfo(sub, finalEmail, null, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
