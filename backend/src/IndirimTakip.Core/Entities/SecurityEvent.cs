namespace IndirimTakip.Core.Entities;

// Security event record: to see abuse and, when needed, back a report with
// evidence.
//
// AN EARLIER DECISION WAS REVERSED ON PURPOSE. When request logging was added
// for sensitive endpoints, the code said "a separate DB table would be
// over-engineering here", and THAT WAS RIGHT AT THE TIME: the goal was only to
// leave a trace in stdout. The goal then changed: the record has to be
// QUERYABLE, DURABLE and defensible. Docker's stdout log meets none of these:
// it rotates, can't be filtered, and is lost when the container is recreated.
//
// NOT EVERY REQUEST IS RECORDED, only noteworthy ones. Normal page views NEVER
// get in here; otherwise the volume would be unmanageable and we would be
// collecting personal data that serves no purpose. A record is only legally
// defensible when its purpose is narrow and defined.
public class SecurityEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// The real client address (Cloudflare's CF-Connecting-IP header). With the
    /// origin locked to Cloudflare, the TCP-level address ALWAYS belongs to
    /// Cloudflare, so this header is the only correct source.
    /// </summary>
    public required string Ip { get; set; }

    /// <summary>
    /// Event kind: <c>unauthorized</c> (unauthorized attempt), <c>rate-limited</c>
    /// (rate limit), <c>probe</c> (known exploit scan), <c>server-error</c>
    /// (server error).
    /// </summary>
    public required string Kind { get; set; }

    public required string Method { get; set; }

    public required string Path { get; set; }

    public int StatusCode { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>
    /// Cloudflare's CF-IPCountry header. Kept because whether an attacker is
    /// domestic or foreign decides which authority a report goes to.
    /// </summary>
    public string? Country { get; set; }
}
