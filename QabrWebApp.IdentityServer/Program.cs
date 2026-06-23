using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using QabrWebApp.IdentityServer.Data;
using QabrWebApp.IdentityServer.Models;
using QabrWebApp.IdentityServer.Seeders;
using QabrWebApp.IdentityServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.UseOpenIddict();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        options.SetTokenEndpointUris("/connect/token");
        options.SetRevocationEndpointUris("/connect/revocation");

        options.AllowPasswordFlow();
        options.AllowRefreshTokenFlow();
        options.AllowCustomFlow("urn:ietf:params:oauth:grant-type:google");
        options.AllowCustomFlow("urn:ietf:params:oauth:grant-type:apple");

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            "qabr-api");

        // Certificat RSA persistant — survit aux redémarrages Docker et compatible JWKS
        var certB64 = builder.Configuration["OpenIddict:CertificatePfxBase64"];
        var certPwd = builder.Configuration["OpenIddict:CertificatePassword"];

        if (!string.IsNullOrEmpty(certB64) && !string.IsNullOrEmpty(certPwd))
        {
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                Convert.FromBase64String(certB64), certPwd,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.MachineKeySet |
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
            options.AddSigningCertificate(cert)
                   .AddEncryptionCertificate(cert);
        }
        else
        {
            // Fallback dev local uniquement
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        }
        options.DisableAccessTokenEncryption();

        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough()
               .DisableTransportSecurityRequirement(); // dev only — HTTP allowed

        options.SetAccessTokenLifetime(TimeSpan.FromHours(1));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

builder.Services.AddHttpClient("external");
builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();
builder.Services.AddHostedService<OpenIddictSeeder>();

var app = builder.Build();

// Auto-migrate Identity DB (crée la base si elle n'existe pas)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var retries = 10;
    while (retries > 0)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex)
        {
            retries--;
            if (retries == 0) { Console.WriteLine($"Migration échouée : {ex.Message}"); break; }
            Console.WriteLine($"SQL Server pas encore prêt ({retries} restants)...");
            Thread.Sleep(3000);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
