using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QabrWebApp.Dal.Entities
{
    public class QabrWebAppDatabaseContextFactory : IDesignTimeDbContextFactory<QabrWebAppDatabaseContext>
    {
        public QabrWebAppDatabaseContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<QabrWebAppDatabaseContext>();
            optionsBuilder.UseSqlServer(
                "Server=.\\SQLExpress;Database=Qabr_Database;Trusted_Connection=True;TrustServerCertificate=True;"
            );
            return new QabrWebAppDatabaseContext(optionsBuilder.Options);
        }
    }
}
