using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
           .EnableDetailedErrors()
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

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
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidateLifetime = true,
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var log = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                log.LogWarning("JWT authentication failed: {Error}", ctx.Exception?.Message);
                return System.Threading.Tasks.Task.CompletedTask;
            },
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

// Feature flags (persistées en BD via AppSettings)
builder.Services.AddSingleton<FeatureFlagsService>();

// WebSocket manager — diffuse les changements de visibilité des commentaires en temps réel
builder.Services.AddSingleton<CommentWebSocketManager>();

// Background services
builder.Services.AddHostedService<PriereJanazaCleanupService>();
builder.Services.AddHostedService<RappelPushBackgroundService>();
builder.Services.AddHostedService<MosqueeDeduplicationBackgroundService>();

// Deduplication service
builder.Services.AddScoped<IMosqueeDeduplicationService, MosqueeDeduplicationService>();

// Géocodage inverse : des coordonnées d'une mosquée vers sa ville et son pays.
// Le User-Agent est EXIGÉ par la politique d'usage de Nominatim — sans lui les
// requêtes sont refusées, pas ralenties.
builder.Services.AddHttpClient<IGeocodageInverseService, GeocodageInverseService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.Add("User-Agent", "QabrApp/1.0");
});
builder.Services.AddScoped<IRattrapageLieuxService, RattrapageLieuxService>();

// Le rattrapage tourne en fond : le même objet est à la fois le service hébergé
// qui travaille et celui que le contrôleur interroge. D'où le singleton
// enregistré une fois, puis exposé sous ses deux visages — sans quoi le
// contrôleur parlerait à une instance différente de celle qui travaille, et
// verrait un état toujours vide.
builder.Services.AddSingleton<RattrapageLieuxWorker>();
builder.Services.AddSingleton<IRattrapageLieuxWorker>(s => s.GetRequiredService<RattrapageLieuxWorker>());
builder.Services.AddHostedService(s => s.GetRequiredService<RattrapageLieuxWorker>());

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

    // ATTENDRE LA BASE, MAIS NE JAMAIS AVALER UN ÉCHEC DE MIGRATION
    // -------------------------------------------------------------
    // La version précédente réessayait dix fois puis démarrait quand même, quel
    // qu'ait été le motif. Deux pannes très différentes se confondaient :
    //
    //   • SQL Server pas encore prêt      -> transitoire, réessayer a du sens
    //   • une migration qui échoue        -> le schéma reste à moitié appliqué
    //
    // Dans le second cas, l'API démarrait sur une base incohérente et renvoyait
    // des 500 sur des routes au hasard, sans qu'aucun message n'ait signalé
    // quoi que ce soit. Une panne bruyante au démarrage se répare en dix
    // minutes ; une base à moitié migrée se cherche pendant des jours.
    //
    // L'attente est donc séparée de la migration : on patiente tant que la
    // connexion n'est pas établie, puis on migre UNE fois. Si cela échoue, on
    // le dit et on refuse de servir.
    var tentatives = 10;
    while (!db.Database.CanConnect() && tentatives > 0)
    {
        tentatives--;
        Console.WriteLine($"SQL Server pas encore prêt, nouvelle tentative dans 5s... ({tentatives} restantes)");
        Thread.Sleep(5000);
    }

    try
    {
        db.Database.Migrate();

        // Crée CommentairesJanaza si elle n'existe pas encore (déploiement fresh)
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND type = 'U')
            BEGIN
                CREATE TABLE CommentairesJanaza (
                    Id                   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    PriereJanazaId       INT NOT NULL,
                    AuteurNom            NVARCHAR(100) NULL,
                    Contenu              NVARCHAR(1000) NOT NULL DEFAULT '',
                    DateCreation         DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    DateModification     DATETIME2 NULL,
                    UtilisateurId        INT NULL,
                    EstCache             BIT NOT NULL DEFAULT 0,
                    ParentCommentaireId  INT NULL,
                    MentionNom           NVARCHAR(100) NULL,
                    CONSTRAINT FK_CommentairesJanaza_PriereJanazaId
                        FOREIGN KEY (PriereJanazaId) REFERENCES PrieresJanaza(Id) ON DELETE CASCADE
                );
            END");

        // Ajoute DateModification si la table existait déjà sans cette colonne
        db.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND type = 'U')
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'CommentairesJanaza')
                               AND name = N'DateModification')
            BEGIN
                ALTER TABLE CommentairesJanaza ADD DateModification datetime2 NULL
            END");

        // Crée LikesCommentaires si elle n'existe pas
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'LikesCommentaires') AND type = 'U')
            BEGIN
                CREATE TABLE LikesCommentaires (
                    Id            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    CommentaireId INT NOT NULL,
                    UtilisateurId INT NULL,
                    DateCreation  DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                    CONSTRAINT FK_LikesCommentaires_CommentaireId
                        FOREIGN KEY (CommentaireId) REFERENCES CommentairesJanaza(Id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX UX_LikesCommentaires_CommentUser
                    ON LikesCommentaires (CommentaireId, UtilisateurId)
                    WHERE UtilisateurId IS NOT NULL;
            END");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine("!!! MIGRATION IMPOSSIBLE — L'APPLICATION NE DÉMARRERA PAS !!!");
        Console.WriteLine($"    {ex.GetType().Name} : {ex.Message}");
        if (ex.InnerException is { } interne)
            Console.WriteLine($"    cause : {interne.GetType().Name} : {interne.Message}");
        Console.WriteLine();
        Console.WriteLine("    Le schéma de la base est peut-être partiellement appliqué.");
        Console.WriteLine("    Vérifiez les migrations déjà enregistrées :");
        Console.WriteLine("      SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;");
        Console.WriteLine();

        // On relance : mieux vaut un container qui redémarre en boucle, visible
        // dans les journaux, qu'une API qui répond à moitié.
        throw;
    }

    // Table AppSettings — feature flags persistés en BD
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.objects
                WHERE object_id = OBJECT_ID(N'AppSettings') AND type = N'U'
            )
            CREATE TABLE [AppSettings] (
                [Key]   nvarchar(100)  NOT NULL,
                [Value] nvarchar(1000) NULL,
                CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Key])
            );
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning AppSettings : {ex.Message}");
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
                    [Id]               INT IDENTITY(1,1)  NOT NULL,
                    [DateCreation]     DATETIME2(7)       NOT NULL,
                    [Genre]            NVARCHAR(10)       NULL,
                    [NomDefunt]        NVARCHAR(200)      NULL,
                    [EstAnonyme]       BIT                NOT NULL DEFAULT 0,
                    [DeclarantPrenom]  NVARCHAR(100)      NULL,
                    [DeclarantNom]     NVARCHAR(100)      NULL,
                    [MosqueeNom]       NVARCHAR(300)      NULL,
                    [Pays]             NVARCHAR(100)      NULL,
                    [VilleEnterrement] NVARCHAR(200)      NULL,
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

    // Colonnes ajoutées progressivement — idempotent
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'PrieresJanaza') AND name = N'VilleEnterrement'
            )
            ALTER TABLE [PrieresJanaza] ADD [VilleEnterrement] nvarchar(200) NULL;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'PrieresJanazaHistorique') AND name = N'VilleEnterrement'
            )
            ALTER TABLE [PrieresJanazaHistorique] ADD [VilleEnterrement] nvarchar(200) NULL;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning VilleEnterrement columns : {ex.Message}");
    }

    // Table commentaires janaza — idempotent
    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.objects
                WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND type = N'U'
            )
            BEGIN
                CREATE TABLE [CommentairesJanaza] (
                    [Id]               INT IDENTITY(1,1)   NOT NULL,
                    [PriereJanazaId]   INT                 NOT NULL,
                    [AuteurNom]        NVARCHAR(100)       NULL,
                    [Contenu]          NVARCHAR(1000)      NOT NULL,
                    [DateCreation]     DATETIME2(7)        NOT NULL,
                    CONSTRAINT [PK_CommentairesJanaza] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_CommentairesJanaza_PrieresJanaza]
                        FOREIGN KEY ([PriereJanazaId])
                        REFERENCES [PrieresJanaza] ([Id])
                        ON DELETE CASCADE
                );
                CREATE INDEX [IX_CommentairesJanaza_PriereJanazaId]
                    ON [CommentairesJanaza] ([PriereJanazaId]);
                PRINT '[Startup] Table CommentairesJanaza créée.';
            END

            -- Colonnes ajoutées après la création initiale
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND name = N'UtilisateurId')
                ALTER TABLE [CommentairesJanaza] ADD [UtilisateurId] INT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND name = N'EstCache')
                ALTER TABLE [CommentairesJanaza] ADD [EstCache] BIT NOT NULL DEFAULT 0;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND name = N'ParentCommentaireId')
                ALTER TABLE [CommentairesJanaza] ADD [ParentCommentaireId] INT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND name = N'MentionNom')
                ALTER TABLE [CommentairesJanaza] ADD [MentionNom] NVARCHAR(100) NULL;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Warning CommentairesJanaza : {ex.Message}");
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

// Colonne IsAdminAuthor sur CommentairesJanaza + backfill
try
{
    using var caScope = app.Services.CreateScope();
    var caDb = caScope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
    caDb.Database.ExecuteSqlRaw(@"
        IF NOT EXISTS (
            SELECT 1 FROM sys.columns
            WHERE object_id = OBJECT_ID(N'CommentairesJanaza') AND name = N'IsAdminAuthor'
        )
        BEGIN
            ALTER TABLE [CommentairesJanaza] ADD [IsAdminAuthor] BIT NOT NULL DEFAULT 0;
            UPDATE c SET c.IsAdminAuthor = 1
            FROM [CommentairesJanaza] c
            INNER JOIN [Utilisateurs] u ON u.Id = c.UtilisateurId
            WHERE u.Role != 'User';
        END
    ");
}
catch (Exception ex) { Console.WriteLine($"[Startup] Warning IsAdminAuthor : {ex.Message}"); }

// Sync du rôle CEO : le seeder IdentityServer ne met pas à jour Utilisateurs.Role
try
{
    using var syncScope = app.Services.CreateScope();
    var syncDb = syncScope.ServiceProvider.GetRequiredService<QabrWebAppDatabaseContext>();
    await syncDb.Utilisateurs
        .Where(u => u.Email == "ceo@salatjanaza.org" && u.Role == "User")
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, "Admin"));
}
catch (Exception ex) { Console.WriteLine($"[Startup] Warning sync CEO role : {ex.Message}"); }

try { await app.Services.GetRequiredService<FeatureFlagsService>().InitAsync(); }
catch (Exception ex) { Console.WriteLine($"[Startup] Warning feature flags: {ex.Message}"); }

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

app.UseWebSockets();
app.UseRouting();
app.UseCors("AllowFront");
app.UseAuthentication();
app.UseAuthorization();

// Endpoint WebSocket pour les mises à jour temps réel des commentaires
app.Map("/ws/comments", async (HttpContext ctx, CommentWebSocketManager mgr) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var id = mgr.Add(ws);
    var buf = new byte[256];
    try
    {
        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
        }
    }
    catch { }
    finally
    {
        mgr.Remove(id);
        if (ws.State != System.Net.WebSockets.WebSocketState.Closed)
        {
            try { await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        }
    }
});

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
