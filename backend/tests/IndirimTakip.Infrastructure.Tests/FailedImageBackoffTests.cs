using IndirimTakip.Infrastructure.Images;

namespace IndirimTakip.Infrastructure.Tests;

public class FailedImageBackoffTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void Failed_product_waits_until_the_backoff_ends_then_is_released()
    {
        var backoff = new FailedImageBackoff();
        backoff.Failed(5694, Start);

        Assert.Contains(5694, backoff.Waiting(Start.AddMinutes(10), Day));
        Assert.Contains(5694, backoff.Waiting(Start.AddHours(23).AddMinutes(59), Day));
        Assert.DoesNotContain(5694, backoff.Waiting(Start.AddHours(24), Day));
    }

    [Fact]
    public void Failing_again_restarts_the_backoff_and_leaves_others_alone()
    {
        var backoff = new FailedImageBackoff();
        backoff.Failed(1, Start);
        backoff.Failed(2, Start);

        // The backoff ended, product 1 was retried and failed again.
        Assert.Empty(backoff.Waiting(Start.AddHours(25), Day));
        backoff.Failed(1, Start.AddHours(25));

        var waiting = backoff.Waiting(Start.AddHours(30), Day);
        Assert.Equal([1], waiting.Order());
    }
}
