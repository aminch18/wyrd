using Microsoft.EntityFrameworkCore;

namespace Telecom.Providers.Persistence;

public sealed class ProviderDb(DbContextOptions<ProviderDb> options) : DbContext(options)
{
    public DbSet<ProviderEffect> Effects => Set<ProviderEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderEffect>().HasKey(effect => effect.Key);
        modelBuilder.Entity<ProviderEffect>().HasIndex(effect => effect.Resource).IsUnique();
    }
}
