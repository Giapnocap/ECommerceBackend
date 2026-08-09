using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ECommerceBackend.Infrastructure.Data
{
    public sealed class AppDbContextDesignTimeFactory
        : IDesignTimeDbContextFactory<AppDbContext>
    {
        private const string DefaultConnectionString =
            "Server=.;Database=ECommerceDB;Trusted_Connection=True;"
            + "TrustServerCertificate=True;Encrypt=False;";

        public AppDbContext CreateDbContext(string[] args)
        {
            var environmentName =
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Development";
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile(
                    $"appsettings.{environmentName}.json",
                    optional: true);

            if (string.Equals(
                environmentName,
                "Development",
                StringComparison.OrdinalIgnoreCase))
            {
                configurationBuilder.AddJsonFile(
                    "appsettings.Local.json",
                    optional: true);
            }

            var configuration = configurationBuilder
                .AddEnvironmentVariables()
                .Build();
            var connectionString =
                configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = DefaultConnectionString;

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new AppDbContext(options);
        }
    }
}
