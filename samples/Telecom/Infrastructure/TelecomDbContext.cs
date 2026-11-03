using Microsoft.EntityFrameworkCore;
using Wyrd.EntityFrameworkCore;

namespace Telecom.Infrastructure;

public sealed class TelecomDbContext(DbContextOptions<TelecomDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddWyrd();
}
