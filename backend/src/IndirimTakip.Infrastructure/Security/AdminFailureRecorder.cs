using IndirimTakip.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndirimTakip.Infrastructure.Security;

/// <summary>
/// Writes failed admin operations to the database.
/// </summary>
/// <remarks>
/// <b>IT OPENS ITS OWN SCOPE, a requirement, not a convenience.</b> Some of the
/// errors to record happen precisely because <c>SaveChangesAsync</c> blew up. The
/// request's own <c>AppDbContext</c> is dirty at that moment: the failed entities
/// are still tracked. Recording through the same context would retry that failed
/// write, so the very record meant to explain the error would hit the same error
/// and be lost. A clean scope breaks that link.
/// </remarks>
public class AdminFailureRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminFailureRecorder> logger)
{
    public async Task RecordAsync(AdminOperationFailure failure, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.AdminOperationFailures.Add(failure);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Failing to record must NOT CHANGE the request's outcome. The admin
            // already gets an error; producing a second one because of the log
            // would bury the real cause entirely.
            logger.LogWarning(ex, "Could not record admin failure: {Method} {Path} {Status}",
                failure.Method, failure.Path, failure.StatusCode);
        }
    }
}
