using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using QabrWebApp.IdentityServer.Models;
using QabrWebApp.IdentityServer.Services;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace QabrWebApp.IdentityServer.Controllers
{
    [ApiController]
    public class TokenController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IExternalAuthService _externalAuth;
        private readonly IConfiguration _config;

        public TokenController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IExternalAuthService externalAuth,
            IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _externalAuth = externalAuth;
            _config = config;
        }

        [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("OpenIddict server request is null.");

            if (request.IsPasswordGrantType())
                return await HandlePasswordAsync(request);

            if (request.IsRefreshTokenGrantType())
                return await HandleRefreshTokenAsync();

            if (request.GrantType == "urn:ietf:params:oauth:grant-type:google")
                return await HandleExternalAsync(request, "Google");

            if (request.GrantType == "urn:ietf:params:oauth:grant-type:apple")
                return await HandleExternalAsync(request, "Apple");

            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // ── Password ──────────────────────────────────────────────────────────────
        private async Task<IActionResult> HandlePasswordAsync(OpenIddictRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Username!)
                       ?? await _userManager.FindByNameAsync(request.Username!);

            if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password!))
                return ForbidWithError(Errors.InvalidGrant, "Email ou mot de passe incorrect.");

            var principal = await BuildPrincipalAsync(user, request.GetScopes());
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // ── Refresh token ─────────────────────────────────────────────────────────
        private async Task<IActionResult> HandleRefreshTokenAsync()
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var userId = result.Principal?.GetClaim(Claims.Subject);
            if (userId is null) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var principal = await BuildPrincipalAsync(user, result.Principal!.GetScopes());
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // ── Google / Apple ────────────────────────────────────────────────────────
        private async Task<IActionResult> HandleExternalAsync(OpenIddictRequest request, string provider)
        {
            var token = request["access_token"]?.ToString()
                        ?? request["id_token"]?.ToString();

            if (string.IsNullOrEmpty(token))
                return ForbidWithError(Errors.InvalidGrant, "Token externe manquant.");

            ExternalUserInfo? info = provider switch
            {
                "Google" => await _externalAuth.ValidateGoogleTokenAsync(token),
                "Apple" => await _externalAuth.ValidateAppleTokenAsync(token),
                _ => null
            };

            if (info is null)
                return ForbidWithError(Errors.InvalidGrant, $"Token {provider} invalide.");

            // Trouver l'utilisateur par login externe, puis par email
            var user = await _userManager.FindByLoginAsync(provider, info.ProviderKey)
                       ?? await _userManager.FindByEmailAsync(info.Email);

            if (user is null)
            {
                // Créer le compte automatiquement
                user = new ApplicationUser
                {
                    UserName = info.Email,
                    Email = info.Email,
                    Prenom = info.FirstName ?? "",
                    Nom = info.LastName ?? "",
                    EmailConfirmed = true,
                };
                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                    return ForbidWithError(Errors.ServerError, "Impossible de créer le compte.");

                // Notification Telegram : nouvel utilisateur via connexion sociale
                _ = QabrWebApp.IdentityServer.Helpers.TelegramHelper.SendNewUserAsync(
                    _config, user.Prenom, user.Nom, user.Email, provider);
            }

            // Lier le login externe s'il n'est pas encore lié
            var existing = await _userManager.FindByLoginAsync(provider, info.ProviderKey);
            if (existing is null)
                await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, info.ProviderKey, provider));

            var principal = await BuildPrincipalAsync(user,
                new[] { Scopes.OpenId, Scopes.Email, Scopes.Profile, "qabr-api" });

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private async Task<ClaimsPrincipal> BuildPrincipalAsync(ApplicationUser user, IEnumerable<string> scopes)
        {
            var principal = await _signInManager.CreateUserPrincipalAsync(user);
            var identity = (ClaimsIdentity)principal.Identity!;

            identity.SetClaim(Claims.Subject, user.Id);
            identity.SetClaim(Claims.Email, user.Email!);
            identity.SetClaim(Claims.Name, $"{user.Prenom} {user.Nom}".Trim());
            identity.SetClaim("prenom", user.Prenom);
            identity.SetClaim("nom", user.Nom);

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
                identity.AddClaim(new System.Security.Claims.Claim(Claims.Role, role.ToLower()));

            principal.SetScopes(scopes);
            principal.SetResources("qabr-api");

            foreach (var claim in principal.Claims)
                claim.SetDestinations(GetDestinations(claim, principal));

            return principal;
        }

        private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
        {
            return claim.Type switch
            {
                Claims.Subject or Claims.Name =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                Claims.Email when principal.HasScope(Scopes.Email) =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                Claims.Role =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                "prenom" or "nom" when principal.HasScope(Scopes.Profile) =>
                    new[] { Destinations.AccessToken, Destinations.IdentityToken },
                _ => new[] { Destinations.AccessToken }
            };
        }

        private IActionResult ForbidWithError(string error, string description)
        {
            var properties = new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            });
            return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
    }
}
