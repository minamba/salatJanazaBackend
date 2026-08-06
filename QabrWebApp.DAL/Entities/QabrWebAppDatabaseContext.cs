using Microsoft.EntityFrameworkCore;

namespace QabrWebApp.Dal.Entities
{
    public partial class QabrWebAppDatabaseContext : DbContext
    {
        public QabrWebAppDatabaseContext(DbContextOptions<QabrWebAppDatabaseContext> options)
            : base(options) { }

        public DbSet<Mosquee>                   Mosquees                  { get; set; }
        public DbSet<PriereJanaza>              PrieresJanaza             { get; set; }
        public DbSet<PriereJanazaHistorique>    PrieresJanazaHistorique   { get; set; }
        public DbSet<Utilisateur>               Utilisateurs              { get; set; }
        public DbSet<Abonnement>                Abonnements               { get; set; }
        public DbSet<RappelPush>                RappelsPush               { get; set; }
        public DbSet<UtilisateurToken>          UtilisateurTokens         { get; set; }
        public DbSet<AppSetting>                AppSettings               { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Mosquee>()
                .HasIndex(m => m.OsmId)
                .IsUnique()
                .HasFilter("[OsmId] IS NOT NULL");

            modelBuilder.Entity<Utilisateur>()
                .HasIndex(u => u.IdentityUserId)
                .IsUnique()
                .HasFilter("[IdentityUserId] IS NOT NULL");

            modelBuilder.Entity<Abonnement>()
                .HasIndex(a => new { a.UtilisateurId, a.MosqueeId })
                .IsUnique();

            modelBuilder.Entity<PriereJanaza>()
                .Property(p => p.Statut)
                .HasConversion<string>();

            modelBuilder.Entity<PriereJanaza>()
                .HasOne(p => p.Mosquee)
                .WithMany(m => m.PrieresJanaza)
                .HasForeignKey(p => p.MosqueeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriereJanaza>()
                .HasOne(p => p.Utilisateur)
                .WithMany(u => u.PrieresDeclarees)
                .HasForeignKey(p => p.UtilisateurId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<RappelPush>()
                .HasIndex(r => new { r.DateEnvoi, r.EnvoyeAt });

            modelBuilder.Entity<RappelPush>()
                .HasOne(r => r.PriereJanaza)
                .WithMany()
                .HasForeignKey(r => r.PriereJanazaId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Abonnement>()
                .HasOne(a => a.Mosquee)
                .WithMany(m => m.Abonnements)
                .HasForeignKey(a => a.MosqueeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Abonnement>()
                .HasOne(a => a.Utilisateur)
                .WithMany(u => u.Abonnements)
                .HasForeignKey(a => a.UtilisateurId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UtilisateurToken>()
                .HasIndex(t => new { t.UtilisateurId, t.ExpoToken })
                .IsUnique();

            modelBuilder.Entity<UtilisateurToken>()
                .HasOne(t => t.Utilisateur)
                .WithMany()
                .HasForeignKey(t => t.UtilisateurId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
