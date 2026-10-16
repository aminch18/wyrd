namespace Wyrd.Tests;

public class LiteralsTests
{
    [Fact]
    public void time_literals_read_like_prose()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), 30.Seconds);
        Assert.Equal(TimeSpan.FromMinutes(15), 15.Minutes);
        Assert.Equal(TimeSpan.FromHours(2), 2.Hours);
        Assert.Equal(TimeSpan.FromDays(7), 7.Days);
    }

    [Fact]
    public void attempts_must_be_positive()
    {
        Assert.Equal(3, 3.Times.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => 0.Times);
    }
}
