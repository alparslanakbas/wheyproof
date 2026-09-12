namespace IndirimTakip.Core.Entities;

/// <summary>
/// The FAILURE REASON of a request to an admin endpoint (<c>/api/dev/*</c>).
/// </summary>
/// <remarks>
/// <b>WHY IT WAS NEEDED.</b> A coupon couldn't be added from the panel and all
/// the screen said was "Coupon not added." The real cause (the catalog name was
/// "Dr Supplement", the typed name "DrSupplement") only surfaced with a manual
/// <c>curl</c>. A second case came the same week: a coupon date sent with a
/// non-UTC offset produced a 500 in Npgsql, and that message too lived only in
/// the container's stdout.
///
/// <b>WHY IT ISN'T WRITTEN TO SecurityEvents.</b> That table's purpose is narrow
/// and defined: evidence of abuse, grounds for a report when needed. The
/// records here are the ADMIN'S OWN operations. It would do three concrete kinds
/// of harm:
/// <list type="bullet">
///   <item>The panel's "top 10 addresses by events" list, the first place to look
///   for an abuse report, would fill up with the admin's own address.</item>
///   <item>Its quota (30 events per address per 5 minutes) is tuned for hostile
///   traffic; a few admin operations tried in a row could silently drop exactly
///   the record needed.</item>
///   <item>SecurityEvents stores NO message or query ON PURPOSE; adding a free text
///   field there would weaken the "kept narrow" rationale. Storing the message
///   here is safe, because the record is produced by the authenticated admin.</item>
/// </list>
///
/// <b>FAILURES ONLY.</b> Successful operations aren't recorded: the question asked
/// is "why didn't it work", and a successful operation shows in the data itself.
/// </remarks>
public class AdminOperationFailure
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Method { get; set; }

    public required string Path { get; set; }

    public int StatusCode { get; set; }

    /// <summary>
    /// The endpoint's own response (e.g. "No brand named 'DrSupplement' was
    /// found.") or, if the operation threw, the exception's type and message.
    /// Null when the endpoint returned an error without a body.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// The requesting address. There is one admin, but it tells errors produced
    /// by scripts apart from those produced by the panel.
    /// </summary>
    public string? Ip { get; set; }
}
