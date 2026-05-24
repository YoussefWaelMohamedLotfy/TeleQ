using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TeleQ.Api.Data;

/// <summary>
/// Design-time factory used by EF Core tools (migrations) to create AppDbContext
/// without needing the full Aspire/application host.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Use a placeholder connection string for design-time; the real one comes from Aspire at runtime.
        optionsBuilder.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__TeleQ-Db") ??
            "Host=localhost;Port=5432;Database=teleq;Username=postgres;Password=postgres");

        return new AppDbContext(optionsBuilder.Options);
    }
}
