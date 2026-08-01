using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QabrWebApp.Builders;
using QabrWebApp.Builders.impl;
using QabrWebApp.Dal.Entities;
using QabrWebApp.Dal.Repositories;
using QabrWebApp.Domain.Repositories;
using QabrWebApp.Domain.Services;
using QabrWebApp.Domain.Services.impl;
using QabrWebApp.Mapper;
using QabrWebApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5168");

Stripe.StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
    options.AddPolicy("AllowFront", policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:8081", "https://salatjanaza.org", "https://www.salatjanaza.org")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Qabr API", Version = "v1" });
    c.EnableAnnotations();
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token JWT émis par IdentityServer (port 5001)",
    });
    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<QabrWebAppDatabaseContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .EnableSensitiveDataLogging()
           .EnableDetailedErrors());

builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MapperProfile>());

// JWT Bearer — validates tokens issued by IdentityServer
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"] ?? "http://localhost:5001";
        options.Audience = "qabr-api";
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
    });

builder.Services.AddAuthorization();

// Repositories
builder.Services.AddScoped<IMosqueeRepository, MosqueeRepository>();
builder.Services.AddScoped<IPriereJanazaRepository, PriereJanazaRepository>();
builder.Services.AddScoped<IUtilisateurRepository, UtilisateurRepository>();
builder.Services.AddScoped<IAbonnementRepository, AbonnementRepository>();
builder.Services.AddScoped<IRappelPushRepository, RappelPushRepository>();
builder.Services.AddScoped<IUtilisateurTokenRepository, UtilisateurTokenRepository>();

// Domain services
builder.Services.AddScoped<IMosqueeService, MosqueeService>();
builder.Services.AddScoped<IPriereJanazaService, PriereJanazaService>();
builder.Services.AddScoped<IUtilisateurService, UtilisateurService>();
builder.Services.AddScoped<IAbonnementService, AbonnementService>();

// ViewModel builders
builder.Services.AddScoped<IMosqueeViewModelBuilder, MosqueeViewModelBuilder>();
builder.Services.AddScoped<IPriereJanazaViewModelBuilder, PriereJanazaViewModelBuilder>();
builder.Services.AddScoped<IUtilisateurViewModelBuilder, UtilisateurViewModelBuilder>();
builder.Services.AddScoped<IAbonnementViewModelBuilder, AbonnementViewModelBuilder>();

// Telegram notification builder (singleton : sans état, dépend uniquement de singletons)
builder.Services.AddSingleton<ITelegramNotificationBuilder, TelegramNotificationBuilder>();

// Feature flags (fichier features.json, pas de migration DB nécessaire)
builder.Services.AddSingleton<FeatureFlagsService>();

// Background services
builder.Services.AddHostedService<PriereJanazaCleanupService>();
builder.Services.AddHostedService<RappelPushBackgroundService>();
builder.Services.AddHostedService<MosqueeDeduplicationBackgroundService>();

// Deduplication service
builder.Services.AddScoped<IMosqueeDeduplicationService, MosqueeDeduplicationService>();

// Import flyer services
builder.Services.AddSingleton<IImportSessionService, ImportSessionService>();
builder.Services.AddSingleton<IFlyerStorageService, GoogleFlyerStorageService>();
builder.Services.AddSingleton<ITextImportStorageService, GoogleTextImportStorageService>();
builder.Services.AddSingleton<ITextImportSummaryService, TextImportSummaryService>();

// Infrastructure services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHttpClient<IOverpassService, OverpassService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.Add("User-Agent", "QabrApp/1.0");
});
builder.Services.AddHttpClient("identity", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["IdentityServer:BaseUrl"] ?? "http://localhost:5001");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<IPushNotificationService, PushNotificationService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
    var retries = 10;
    while (retries > 0)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception)
        {
            retries--;
            if (retries == 0) break;
            Console.WriteLine($"SQL Server pas encore prêt, nouvelle tentative dans 5s... ({retries} restants)");
            Thread.Sleep(5000);
        }
    }

    // Colonnes ajoutées manuellement — idempotent, safe à rejouer à chaque démarrage
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'Utilisateurs') AND name = N'Platform'
            )
            ALTER TABLE [Utilisateurs] ADD [Platform] nvarchar(10) NULL;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning colonnes optionnelles : {ex.Message}");
    }

    // Table historique des janazas — immunisée à la purge, jamais supprimée
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.objects
                WHERE object_id = OBJECT_ID(N'PrieresJanazaHistorique') AND type = N'U'
            )
            BEGIN
                CREATE TABLE [PrieresJanazaHistorique] (
                    [Id]              INT IDENTITY(1,1)  NOT NULL,
                    [DateCreation]    DATETIME2(7)       NOT NULL,
                    [Genre]           NVARCHAR(10)       NULL,
                    [NomDefunt]       NVARCHAR(200)      NULL,
                    [EstAnonyme]      BIT                NOT NULL DEFAULT 0,
                    [DeclarantPrenom] NVARCHAR(100)      NULL,
                    [DeclarantNom]    NVARCHAR(100)      NULL,
                    [MosqueeNom]      NVARCHAR(300)      NULL,
                    [Pays]            NVARCHAR(100)      NULL,
                    CONSTRAINT [PK_PrieresJanazaHistorique] PRIMARY KEY ([Id])
                );
                CREATE INDEX [IX_PrieresJanazaHistorique_DateCreation]
                    ON [PrieresJanazaHistorique] ([DateCreation]);
            END
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning PrieresJanazaHistorique : {ex.Message}");
    }

    // Backfill historique — exécuté une seule fois si la table est vide
    // Rétroalimente toutes les janazas existantes avant le déploiement de cette feature
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT TOP 1 1 FROM [PrieresJanazaHistorique])
            BEGIN
                INSERT INTO [PrieresJanazaHistorique]
                    ([DateCreation], [Genre], [NomDefunt], [EstAnonyme],
                     [DeclarantPrenom], [DeclarantNom], [MosqueeNom], [Pays])
                SELECT
                    p.[DateCreation],
                    p.[Genre],
                    p.[NomDefunt],
                    p.[EstAnonyme],
                    u.[Prenom],
                    u.[Nom],
                    m.[Nom],
                    p.[PaysEnterrement]
                FROM [PrieresJanaza] p
                LEFT JOIN [Utilisateurs] u ON u.[Id] = p.[UtilisateurId]
                LEFT JOIN [Mosquees]     m ON m.[Id] = p.[MosqueeId];

                PRINT CONCAT('[Startup] Backfill historique : ',
                    CAST(@@ROWCOUNT AS NVARCHAR), ' janazas importées.');
            END
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning backfill historique : {ex.Message}");
    }
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception");
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                error = ex?.Message,
                detail = ex?.InnerException?.Message
            }));
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors("AllowFront");
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

// Exposer le dossier flyers en dehors de wwwroot si besoin
var flyersPath = builder.Configuration["Flyers:StoragePath"];
if (!string.IsNullOrEmpty(flyersPath) && Directory.Exists(flyersPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(flyersPath),
        RequestPath = "/flyers"
    });
}
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
