using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace IndirimTakip.Infrastructure.Security;

/// <summary>
/// Builds the REASON text recorded for a failed admin operation.
/// </summary>
/// <remarks>
/// Kept as a pure function because the whole feature depends on it: if the
/// reason comes out wrong, the panel shows an empty column and the admin is back
/// to guessing, so the record exists but is useless. Being pure, it can be tested.
/// </remarks>
public static class AdminFailureReason
{
    /// <summary>Limit of the reason text and of the <c>Reason</c> column.</summary>
    public const int MaxLength = 2000;

    // System.Text.Json escapes every non-ASCII character BY DEFAULT: a brand
    // name such as "GHOST®" or "Café" would be stored as "GHOST®". The
    // record would be technically right but unreadable in the panel, which
    // defeats a field kept to explain the error. Escaping is relaxed for Latin
    // letters and symbols.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA),
    };

    /// <summary>
    /// Reason text from the value an endpoint returned. Non-text values are
    /// serialized to JSON: sturdier than guessing a field name, because the text
    /// is recorded whatever shape the endpoint uses.
    /// </summary>
    public static string? FromValue(object? value)
    {
        if (value is null)
            return null;

        if (value is string text)
            return Truncate(text);

        try
        {
            return Truncate(JsonSerializer.Serialize(value, JsonOptions));
        }
        catch (Exception)
        {
            // Losing the whole record over a value that can't be serialized isn't
            // worth it; the status code and path are still recorded.
            return null;
        }
    }

    /// <summary>Reason text from an exception.</summary>
    public static string FromException(Exception ex)
    {
        var text = ex.GetType().Name + ": " + ex.Message;

        // THE REAL CAUSE IS OFTEN INSIDE. EF and Npgsql wrap the error; Npgsql's
        // "only offset 0 (UTC) is supported" was exactly such an inner exception,
        // and the outer message alone said nothing.
        if (ex.InnerException is { } inner)
            text += " → " + inner.GetType().Name + ": " + inner.Message;

        return Truncate(text)!;
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return value.Length <= MaxLength ? value : value[..MaxLength];
    }
}
