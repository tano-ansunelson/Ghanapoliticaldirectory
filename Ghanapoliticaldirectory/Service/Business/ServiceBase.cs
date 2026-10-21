using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service.Business;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ghanapoliticaldirectory.Business;

/// <summary>Shared plumbing: permission checks, audit entries, revisions and the save-or-roll-back wrapper.</summary>
public abstract class ServiceBase
{
    protected readonly AppDbContext Db;
    protected readonly ICurrentUser User;
    protected readonly IClock Clock;
    protected readonly WorkflowOptions Options;

    protected ServiceBase(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
    {
        Db = db;
        User = user;
        Clock = clock;
        Options = options;
    }

    protected DateTime Now => Clock.UtcNow;
    protected DateOnly Today => DateOnly.FromDateTime(Clock.UtcNow);

    protected bool Can(string permission) => User.Permissions.Contains(permission);

    protected static ServiceResult<T> Denied<T>(string message) => Result.Fail<T>(ServiceFailure.Forbidden, "forbidden", message);
    protected static ServiceResult Denied(string message) => Result.Fail(ServiceFailure.Forbidden, "forbidden", message);

    protected static ServiceResult<T> Missing<T>(string what) => Result.Fail<T>(ServiceFailure.NotFound, "not_found", $"{what} was not found.");
    protected static ServiceResult Missing(string what) => Result.Fail(ServiceFailure.NotFound, "not_found", $"{what} was not found.");

    /// <summary>Records that are still being entered need the data-entry permission; reviewed ones need the correction permission.</summary>
    protected bool CanEditRecord(WorkflowStage stage) =>
        stage <= WorkflowStage.AwaitingVerification ? Can(Permissions.RecordsEnter) : Can(Permissions.RecordsCorrect);

    protected void Audit(string action, string entityType, int? entityId, string summary, object? details = null)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            AtUtc = Now,
            UserId = User.UserId,
            UserName = User.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary.Length > 1000 ? summary[..1000] : summary,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = User.IpAddress
        });
    }

    /// <summary>Keeps the previous values of an important record so a correction never destroys history.</summary>
    protected async Task AddRevisionAsync(string entityType, int entityId, object snapshot, string reason, CancellationToken ct)
    {
        var next = await Db.RecordRevisions.CountAsync(r => r.EntityType == entityType && r.EntityId == entityId, ct) + 1;
        Db.RecordRevisions.Add(new RecordRevision
        {
            EntityType = entityType,
            EntityId = entityId,
            RevisionNumber = next,
            SnapshotJson = JsonSerializer.Serialize(snapshot),
            Reason = reason,
            ChangedBy = User.UserId ?? "system",
            ChangedAtUtc = Now
        });
    }

    /// <summary>
    /// Runs the work in one transaction. If it reports a problem or throws, nothing is saved and the change
    /// tracker is cleared so half-built records cannot leak into a later save on the same context.
    /// </summary>
    protected async Task<TResult> InTransactionAsync<TResult>(Func<Task<TResult>> work, CancellationToken ct)
        where TResult : ServiceResult, new()
    {
        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work();
            if (result.Ok)
            {
                await tx.CommitAsync(ct);
            }
            else
            {
                await tx.RollbackAsync(ct);
                Db.ChangeTracker.Clear();
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            Db.ChangeTracker.Clear();
            var r = new TResult { Failure = ServiceFailure.Conflict };
            r.Report.Error("conflict", "Someone else changed this record while you were working. Reload it and try again.");
            return r;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            Db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>Finds a source by address or creates one. Returns null when no source was given.</summary>
    protected async Task<Source?> ResolveSourceAsync(string? title, string? url, CancellationToken ct)
    {
        title = title?.Trim();
        url = url?.Trim();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(url)) return null;

        if (!string.IsNullOrEmpty(url))
        {
            var pending = Db.Sources.Local.FirstOrDefault(s => s.Url == url);
            if (pending is not null) return pending;
            var existing = await Db.Sources.FirstOrDefaultAsync(s => s.Url == url, ct);
            if (existing is not null) return existing;
        }

        var source = new Source { Title = string.IsNullOrEmpty(title) ? url! : title, Url = string.IsNullOrEmpty(url) ? null : url };
        Db.Sources.Add(source);
        return source;
    }
}
