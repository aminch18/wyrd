using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Api.Durable;

/// <summary>For <c>dotnet ef migrations add</c>: the model only, no database is opened.</summary>
public sealed class DesignTimeShopDbContext : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql("Host=localhost;Database=shop").Options);
}
