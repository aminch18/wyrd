using Microsoft.EntityFrameworkCore;
using Wyrd.EntityFrameworkCore;

namespace Shop.Api.Durable;

/// <summary>
/// The shop's database. It has no tables of its own yet, only Wyrd's: they are part of this model, so they are part of
/// its migrations, and a table added here later commits in the same transaction as Wyrd's.
/// </summary>
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddWyrd();
}
