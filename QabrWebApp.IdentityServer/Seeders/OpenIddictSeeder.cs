using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using QabrWebApp.IdentityServer.Models;

namespace QabrWebApp.IdentityServer.Seeders
{
    public class OpenIddictSeeder : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public OpenIddictSeeder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();

            // Seed OpenIddict application
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = "qabr-mobile",
                ClientType = OpenIddictConstants.ClientTypes.Public,
                DisplayName = "Qabr Mobile App",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.Password,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.Prefixes.GrantType + "urn:ietf:params:oauth:grant-type:google",
                    OpenIddictConstants.Permissions.Prefixes.GrantType + "urn:ietf:params:oauth:grant-type:apple",
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "qabr-api",
                },
            };

            var existing = await manager.FindByClientIdAsync("qabr-mobile", cancellationToken);
            if (existing is null)
                await manager.CreateAsync(descriptor, cancellationToken);
            else
                await manager.UpdateAsync(existing, descriptor, cancellationToken);

            // Seed Admin role and admin user
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole("Admin"));

            const string adminEmail = "ceo@salatjanaza.org";
            const string adminPassword = "ceosalatjanaza2026@!!@";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser is null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    Prenom = "CEO",
                    Nom = "Admin",
                    EmailConfirmed = true,
                };
                await userManager.CreateAsync(adminUser, adminPassword);
            }

            if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
                await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
