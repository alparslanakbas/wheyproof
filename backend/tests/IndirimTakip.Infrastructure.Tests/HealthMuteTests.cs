using IndirimTakip.Api.Endpoints;
using Microsoft.Extensions.Configuration;

namespace IndirimTakip.Infrastructure.Tests;

// The health endpoint's alarm decision and timed mutes. An alarm that stays
// silently green is more dangerous than one stuck red: most tests check that
// a mute does not silence more than it should.
public class HealthMuteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static HealthEndpoints.SourceState Source(string name, double hoursAgo) =>
        new(name, Now.AddHours(-hoursAgo), 10);

    private static IReadOnlyList<HealthEndpoints.SourceMute> Read(params (string Key, string Value)[] settings) =>
        HealthEndpoints.ReadMutes(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build());

    private static readonly HealthEndpoints.SourceMute[] KagedMuted =
        [new("Kaged", new DateOnly(2026, 11, 15))];

    // 15 Oct 2026: Kaged is the known failure (a Cloudflare challenge); another
    // store breaking the same day must still turn the monitor red.
    [Fact]
    public void MutedSourceGivesNo503ButAnotherStaleSourceDoes()
    {
        var result = HealthEndpoints.Classify(
            [Source("Kaged", 36), Source("Momentous", 30), Source("Myprotein", 5)], Now, 26, KagedMuted);

        Assert.Equal(["Momentous"], result.Stale.Select(s => s.Source));
        var muted = Assert.Single(result.Muted);
        Assert.Equal("Kaged", muted.Source.Source);
        Assert.Equal(new DateOnly(2026, 11, 15), muted.Until);
    }

    // The end day is included (UTC); the day after, the source raises the
    // alarm again. There is no open-ended mute.
    [Theory]
    [InlineData("2026-10-15", false)]
    [InlineData("2026-10-14", true)]
    public void MuteIncludesItsEndDayThenTheAlarmComesBack(string until, bool alarm)
    {
        var mutes = Read(("Health:Muted:0:Source", "Kaged"), ("Health:Muted:0:Until", until));

        var result = HealthEndpoints.Classify([Source("Kaged", 36)], Now, 26, mutes);

        Assert.Equal(alarm, result.Stale.Count == 1);
    }

    [Fact]
    public void NameMustMatchExactly()
    {
        var result = HealthEndpoints.Classify([Source("kaged", 36), Source("Kaged ", 36)], Now, 26, KagedMuted);

        Assert.Equal(2, result.Stale.Count);
        Assert.Empty(result.Muted);
    }

    [Fact]
    public void FreshSourceIsInNoListEvenWhenMuted()
    {
        var result = HealthEndpoints.Classify([Source("Kaged", 2)], Now, 26, KagedMuted);

        Assert.Empty(result.Stale);
        Assert.Empty(result.Muted);
    }

    // A broken entry is ignored rather than failing start-up: the safe
    // direction is the alarm ringing on.
    [Fact]
    public void BrokenEntriesAreIgnored()
    {
        var mutes = Read(
            ("Health:Muted:0:Source", "Kaged"), ("Health:Muted:0:Until", "15.11.2026"),
            ("Health:Muted:1:Source", "Momentous"),
            ("Health:Muted:2:Until", "2026-11-15"),
            ("Health:Muted:3:Source", " Huel "), ("Health:Muted:3:Until", "2026-11-15"));

        Assert.Equal([new HealthEndpoints.SourceMute("Huel", new DateOnly(2026, 11, 15))], mutes);
    }

    [Fact]
    public void RetiredSourceGivesNo503()
    {
        var result = HealthEndpoints.Classify([Source("Old", 24 * 31), Source("Fresh", 1)], Now, 26, []);

        Assert.Empty(result.Stale);
        Assert.Equal(["Old"], result.Retired);
    }

    // Every production entry must be valid: a broken one is silently ignored,
    // so a typo would leave open an alarm someone believes muted.
    [Fact]
    public void ProductionMutesAreValid()
    {
        var settings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Production.json")
            .Build();

        var entries = settings.GetSection("Health:Muted").GetChildren().Count();

        Assert.True(entries > 0, "the production settings should carry the Kaged mute");
        Assert.Equal(entries, HealthEndpoints.ReadMutes(settings).Count);
    }
}
