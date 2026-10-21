using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghanapoliticaldirectory.Business;

public enum WorkflowEntity { Person, Appointment, ParliamentaryTerm, PartyAffiliation, Candidate, ElectionCycle, Administration, ElectionResult }

public sealed record AdvanceOutcome(WorkflowStage NewStage, int Changed, IReadOnlyList<string> Skipped);

public sealed record ReviewQueueRow(WorkflowEntity Type, int Id, string Label, WorkflowStage Stage, string? EnteredBy, DateTime LastChangedUtc);

/// <summary>
/// One place that moves any record through Draft → Verified → Approved → Published.
/// Permissions, distinct-user rules and "what must already be public" are all checked here, on the server.
/// </summary>
public sealed class WorkflowService : ServiceBase
{
    private readonly ResultService _results;

    public WorkflowService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options, ResultService results)
        : base(db, user, clock, options) => _results = results;

    public async Task<ServiceResult<AdvanceOutcome>> AdvanceAsync(
        WorkflowEntity type, int id, WorkflowAction action, string? notes, bool includeRelated = false, CancellationToken ct = default)
    {
        return await InTransactionAsync(async () =>
        {
            if (type == WorkflowEntity.ElectionResult)
            {
                var r = await Db.ElectionResults
                    .Include(x => x.Lines).Include(x => x.Sources)
                    .Include(x => x.ElectionContest).ThenInclude(c => c.ElectionCycle)
                    .FirstOrDefaultAsync(x => x.Id == id, ct);
                if (r is null) return Missing<AdvanceOutcome>("That result");
                var res = await _results.AdvanceLoadedAsync(r, action, notes, ct);
                if (!res.Ok) return new ServiceResult<AdvanceOutcome> { Failure = res.Failure, Report = res.Report };
                return Result.Ok(new AdvanceOutcome(res.Value, 1, Array.Empty<string>()), res.Report);
            }

            var record = await LoadAsync(type, id, ct);
            if (record is null) return Missing<AdvanceOutcome>("That record");
            if (record.IsArchived)
                return Result.Fail<AdvanceOutcome>(ServiceFailure.Conflict, "workflow.archived", "This record is archived. Restore it first.");
            if (WorkflowStamp.NeedsReason(action) && string.IsNullOrWhiteSpace(notes))
                return Result.Fail<AdvanceOutcome>(ServiceFailure.Invalid, "workflow.reason_required", "Give a reason so the person who entered it knows what to fix.");

            var decision = Decide(record, action);
            if (!decision.Allowed) return Result.Fail<AdvanceOutcome>(ServiceFailure.Forbidden, "workflow.denied", decision.Reason!);

            var blocker = action == WorkflowAction.Publish ? await PublishBlockerAsync(type, record, ct) : null;
            if (blocker is not null) return Result.Fail<AdvanceOutcome>(ServiceFailure.Conflict, "workflow.publish_blocked", blocker);

            var changed = 0;
            var skipped = new List<string>();
            Apply(record, action, decision.NewStage!.Value, notes, type, id);
            changed++;

            if (includeRelated && type == WorkflowEntity.Person)
            {
                // Person first: the other records may only be published once the person is.
                var person = (Person)record;
                await Db.Entry(person).Collection(p => p.Affiliations).LoadAsync(ct);
                await Db.Entry(person).Collection(p => p.Appointments).LoadAsync(ct);
                await Db.Entry(person).Collection(p => p.ParliamentaryTerms).LoadAsync(ct);
                await Db.Entry(person).Collection(p => p.Candidacies).LoadAsync(ct);

                var related = new List<(WorkflowEntity T, PublishableEntity E, string L)>();
                related.AddRange(person.Affiliations.Select(x => (WorkflowEntity.PartyAffiliation, (PublishableEntity)x, "Party affiliation")));
                related.AddRange(person.ParliamentaryTerms.Select(x => (WorkflowEntity.ParliamentaryTerm, (PublishableEntity)x, "Parliamentary term")));
                related.AddRange(person.Candidacies.Select(x => (WorkflowEntity.Candidate, (PublishableEntity)x, "Candidacy")));
                related.AddRange(person.Appointments.Select(x => (WorkflowEntity.Appointment, (PublishableEntity)x, "Appointment")));

                foreach (var (t, e, label) in related.Where(r => !r.E.IsArchived))
                {
                    var d = Decide(e, action);
                    if (!d.Allowed) { skipped.Add($"{label} #{e.Id}: {d.Reason}"); continue; }
                    var b = action == WorkflowAction.Publish ? await PublishBlockerAsync(t, e, ct) : null;
                    if (b is not null) { skipped.Add($"{label} #{e.Id}: {b}"); continue; }
                    Apply(e, action, d.NewStage!.Value, notes, t, e.Id);
                    changed++;
                }
            }

            await Db.SaveChangesAsync(ct);
            return Result.Ok(new AdvanceOutcome(record.Stage, changed, skipped));
        }, ct);
    }

    private WorkflowDecision Decide(PublishableEntity e, WorkflowAction action) =>
        WorkflowRules.Evaluate(WorkflowStamp.Snapshot(e), action, User.UserId ?? "", User.Permissions, Options.RequireDistinctUsers);

    private void Apply(PublishableEntity e, WorkflowAction action, WorkflowStage next, string? notes, WorkflowEntity type, int id)
    {
        var before = e.Stage;
        WorkflowStamp.Apply(e, action, next, User.UserId!, Now, notes);
        Audit($"{type.ToString().ToLowerInvariant()}.{action.ToString().ToLowerInvariant()}", type.ToString(), id,
            $"{type} #{id}: {before} → {next}", new { notes });
    }

    private async Task<PublishableEntity?> LoadAsync(WorkflowEntity type, int id, CancellationToken ct)
    {
        switch (type)
        {
            case WorkflowEntity.Person:
                return await Db.People.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.Appointment: 
                return await Db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.ParliamentaryTerm:
                return await Db.ParliamentaryTerms.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.PartyAffiliation:
                return await Db.PartyAffiliations.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.Candidate: 
                return await Db.Candidates.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.ElectionCycle:
                return await Db.ElectionCycles.FirstOrDefaultAsync(x => x.Id == id, ct);
            case WorkflowEntity.Administration:
                return await Db.Administrations.FirstOrDefaultAsync(x => x.Id == id, ct);
            default: return null;
        }
    }

    /// <summary>Nothing may be public while the thing it hangs off is not. Checks read the loaded objects, so a person published a moment ago in the same request counts.</summary>
    private async Task<string?> PublishBlockerAsync(WorkflowEntity type, PublishableEntity e, CancellationToken ct)
    {
        async Task<string?> PersonOk(int personId)
        {
            var p = await Db.People.FirstOrDefaultAsync(x => x.Id == personId, ct);
            return p is { IsArchived: false, Stage: WorkflowStage.Published } ? null : "Publish the person's profile first.";
        }

        switch (type)
        {
            case WorkflowEntity.PartyAffiliation: return await PersonOk(((PartyAffiliation)e).PersonId);
            case WorkflowEntity.ParliamentaryTerm: return await PersonOk(((ParliamentaryTerm)e).PersonId);
            case WorkflowEntity.Candidate:
                {
                    var c = (Candidate)e;
                    var person = await PersonOk(c.PersonId);
                    if (person is not null) return person;
                    var contest = await Db.ElectionContests.Include(x => x.ElectionCycle).FirstAsync(x => x.Id == c.ElectionContestId, ct);
                    return contest.ElectionCycle.Stage == WorkflowStage.Published ? null : "Publish the election cycle first.";
                }
            case WorkflowEntity.Appointment:
                {
                    var a = (Appointment)e;
                    var person = await PersonOk(a.PersonId);
                    if (person is not null) return person;
                    if (a.AdministrationId is int aid)
                    {
                        var admin = await Db.Administrations.FirstAsync(x => x.Id == aid, ct);
                        if (admin.Stage != WorkflowStage.Published || admin.IsArchived) return "Publish the administration first.";
                    }
                    return null;
                }
            default: return null;
        }
    }

    /// <summary>Records waiting for someone, newest first. Pass a stage to narrow to one step.</summary>
    public async Task<IReadOnlyList<ReviewQueueRow>> GetQueueAsync(WorkflowStage? stage = null, int take = 100, CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return Array.Empty<ReviewQueueRow>();
        var wanted = stage;
        var rows = new List<ReviewQueueRow>();

        // With no filter the queue shows what is waiting on a reviewer: everything from draft up to (not including) published.
        var stages = wanted is null
            ? new List<WorkflowStage> { WorkflowStage.Draft, WorkflowStage.AwaitingVerification, WorkflowStage.Verified, WorkflowStage.Approved }
            : new List<WorkflowStage> { wanted.Value };

        rows.AddRange((await Db.People.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, x.FullName, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.Person, x.Id, x.FullName, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.Appointments.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, Label = x.Person.FullName + " — " + x.Position.Title, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.Appointment, x.Id, x.Label, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.ParliamentaryTerms.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, Label = x.Person.FullName + " — MP, " + x.Constituency.Name, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.ParliamentaryTerm, x.Id, x.Label, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.PartyAffiliations.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, Label = x.Person.FullName + " — party affiliation", x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.PartyAffiliation, x.Id, x.Label, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.Candidates.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, Label = x.Person.FullName + " — candidate, " + x.ElectionContest.ElectionCycle.Name, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.Candidate, x.Id, x.Label, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.ElectionResults.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, Label = "Result — " + x.ElectionContest.ElectionCycle.Name + (x.ElectionContest.Constituency != null ? ", " + x.ElectionContest.Constituency.Name : ", presidential"), x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.ElectionResult, x.Id, x.Label, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.ElectionCycles.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, x.Name, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.ElectionCycle, x.Id, x.Name, x.Stage, x.CreatedBy, x.At)));

        rows.AddRange((await Db.Administrations.AsNoTracking().Where(x => !x.IsArchived && stages.Contains(x.Stage)).OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).Take(take)
            .Select(x => new { x.Id, x.Name, x.Stage, x.CreatedBy, At = x.UpdatedAtUtc ?? x.CreatedAtUtc }).ToListAsync(ct))
            .Select(x => new ReviewQueueRow(WorkflowEntity.Administration, x.Id, x.Name, x.Stage, x.CreatedBy, x.At)));

        return rows.OrderByDescending(r => r.LastChangedUtc).Take(take).ToList();
    }
}
