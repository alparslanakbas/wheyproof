using IndirimTakip.Infrastructure.Security;

namespace IndirimTakip.Infrastructure.Tests;

/// <summary>
/// Reason text extraction. The whole feature depends on it: an empty or
/// unreadable reason leaves a record in the panel that is useless, and the admin
/// is back to guessing.
/// </summary>
public class AdminFailureReasonTests
{
    [Fact]
    public void Text_passes_through_unchanged()
    {
        var reason = AdminFailureReason.FromValue("No brand named 'DrSupplement' was found.");

        Assert.Equal("No brand named 'DrSupplement' was found.", reason);
    }

    [Fact]
    public void Empty_value_returns_null()
    {
        Assert.Null(AdminFailureReason.FromValue(null));
        Assert.Null(AdminFailureReason.FromValue(string.Empty));
    }

    /// <summary>
    /// System.Text.Json escapes non-ASCII by default; without this the panel
    /// would show "GHOST®", technically right and unreadable.
    /// </summary>
    [Fact]
    public void Non_ascii_letters_are_not_escaped_when_serializing_an_object()
    {
        var reason = AdminFailureReason.FromValue(new { message = "Brand not found: GHOST® Café Blend" });

        Assert.NotNull(reason);
        Assert.Contains("GHOST®", reason);
        Assert.Contains("Café", reason);
        Assert.DoesNotContain("\\u", reason);
    }

    [Fact]
    public void Long_text_is_truncated_at_the_column_limit()
    {
        var reason = AdminFailureReason.FromValue(new string('x', AdminFailureReason.MaxLength + 500));

        Assert.Equal(AdminFailureReason.MaxLength, reason!.Length);
    }

    /// <summary>
    /// A real case: Npgsql's "only offset 0 (UTC) is supported" message was in the
    /// INNER exception, and the outer one said nothing.
    /// </summary>
    [Fact]
    public void Inner_exception_message_is_recorded_too()
    {
        var inner = new ArgumentException("Cannot write DateTimeOffset with Offset=+03:00 to PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.");
        var outer = new InvalidOperationException("An error occurred while saving the entity changes.", inner);

        var reason = AdminFailureReason.FromException(outer);

        Assert.Contains("InvalidOperationException", reason);
        Assert.Contains("ArgumentException", reason);
        Assert.Contains("only offset 0 (UTC) is supported", reason);
    }

    [Fact]
    public void Without_an_inner_exception_it_stays_one_line()
    {
        var reason = AdminFailureReason.FromException(new InvalidOperationException("on its own"));

        Assert.Equal("InvalidOperationException: on its own", reason);
    }

    [Fact]
    public void Very_long_exception_text_is_truncated_too()
    {
        var reason = AdminFailureReason.FromException(new InvalidOperationException(new string('y', 5000)));

        Assert.Equal(AdminFailureReason.MaxLength, reason.Length);
    }
}
