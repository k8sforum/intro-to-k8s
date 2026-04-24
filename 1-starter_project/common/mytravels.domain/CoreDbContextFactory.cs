using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace mytravels.domain
{
    public class CoreDbContextFactory : IDesignTimeDbContextFactory<CoreDbContext>
    {
        public CoreDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<CoreDbContext>();
            var connectionString = configuration.GetConnectionString("CoreDbContext")
                ?? throw new InvalidOperationException("Connection string 'CoreDbContext' not found.");

            optionsBuilder.UseNpgsql(connectionString, options =>
            {
                options.MigrationsHistoryTable("EFMigrationsHistory", schema: "config");
                options.EnableRetryOnFailure();
            });

            return new CoreDbContext(optionsBuilder.Options);
        }
    }
}
