using Microsoft.EntityFrameworkCore;
using Shop;
using Wyrd.EntityFrameworkCore;

namespace Wyrd.EntityFrameworkCore.Tests;

public sealed class ConflictDelayTests
{
    private static readonly TimeSpan First = TimeSpan.FromMilliseconds(5);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void a_retry_waits_at_most_the_first_delay_doubled_for_each_retry_before_it(int retry, int mostMs)
    {
        for (var i = 0; i < 1_000; i++)
        {
            var wait = EfCoreRuntime<WyrdDbContext>.ConflictDelay(Guid.CreateVersion7(), retry, First);
            Assert.InRange(wait, TimeSpan.Zero, TimeSpan.FromMilliseconds(mostMs));
        }
    }

    [Fact]
    public void the_same_command_waits_the_same_and_different_commands_spread_out()
    {
        var id = Guid.CreateVersion7();
        Assert.Equal(EfCoreRuntime<WyrdDbContext>.ConflictDelay(id, 3, First), EfCoreRuntime<WyrdDbContext>.ConflictDelay(id, 3, First));

        // Callers that collided must not come back together: across commands, the waits cover the whole range.
        var waits = Enumerable.Range(0, 1_000)
            .Select(_ => EfCoreRuntime<WyrdDbContext>.ConflictDelay(Guid.CreateVersion7(), 1, First).TotalMilliseconds)
            .ToList();
        Assert.True(waits.Min() < 1 && waits.Max() > 4, $"waits from {waits.Min()} to {waits.Max()} ms");
        Assert.True(waits.Distinct().Count() > 900);
    }

    [Fact]
    public void no_first_delay_means_retrying_at_once() =>
        Assert.Equal(TimeSpan.Zero, EfCoreRuntime<WyrdDbContext>.ConflictDelay(Guid.CreateVersion7(), 4, TimeSpan.Zero));

    [Fact]
    public void a_negative_first_delay_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfCoreRuntime<WyrdDbContext>(
            new NoContexts(), ShopJson.Default, Database.Retention, Database.Options with { ConflictRetryDelay = TimeSpan.FromMilliseconds(-1) }));

    private sealed class NoContexts : IDbContextFactory<WyrdDbContext>
    {
        public WyrdDbContext CreateDbContext() => throw new InvalidOperationException("Not used.");
    }
}
