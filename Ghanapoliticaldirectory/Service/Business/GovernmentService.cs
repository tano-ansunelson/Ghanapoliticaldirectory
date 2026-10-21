using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

/// <summary>
/// Changes to who holds office. Ending one record never touches the person's other records, and nothing is
/// deleted: ended offices stay in the archive with their dates.
/// </summary>
public sealed class GovernmentService : ServiceBase
{
    public GovernmentService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    // ------------------------------------------------------------------ ending one record

    public async Task<ServiceResult> EndAppointmentAsync(int appointmentId, DateOnly endDate, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail(ServiceFailure.Invalid, "end.reason_required", "Give the reason the appointment ended.");
        var a = await Db.Appointments.Include(x => x.Position).FirstOrDefaultAsync(x => x.Id == appointmentId, ct);
        if (a is null) return Missing("That appointment");
        if (!CanEditRecord(a.Stage)) return Denied("You do not have permission to change this record.");
        if (a.EndDate is not null) return Result.Fail(ServiceFailure.Conflict, "end.already_ended", "This appointment already has an end date.");
        if (endDate < a.StartDate) return Result.Fail(ServiceFailure.Invalid, "end.before_start", "The end date cannot be before the start date.");

        return await InTransactionAsync(async () =>
        {
            await SnapshotIfReviewedAsync(a, a.Stage, new { a.EndDate, a.Status, a.EndReason }, $"Ended: {reason.Trim()}", ct);
            CloseAppointment(a, endDate, reason.Trim());
            Audit("appointment.ended", nameof(Appointment), a.Id, $"Ended {a.Position.Title} (appointment #{a.Id}) on {endDate:yyyy-MM-dd}: {reason.Trim()}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    public async Task<ServiceResult> EndTermAsync(int termId, DateOnly endDate, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail(ServiceFailure.Invalid, "end.reason_required", "Give the reason the term ended.");
        var t = await Db.ParliamentaryTerms.FirstOrDefaultAsync(x => x.Id == termId, ct);
        if (t is null) return Missing("That parliamentary term");
        if (!CanEditRecord(t.Stage)) return Denied("You do not have permission to change this record.");
        if (t.EndDate is not null) return Result.Fail(ServiceFailure.Conflict, "end.already_ended", "This term already has an end date.");
        if (endDate < t.StartDate) return Result.Fail(ServiceFailure.Invalid, "end.before_start", "The end date cannot be before the start date.");

        return await InTransactionAsync(async () =>
        {
            await SnapshotIfReviewedAsync(t, t.Stage, new { t.EndDate, t.Notes }, $"Ended: {reason.Trim()}", ct);
            t.EndDate = endDate;
            t.Notes = string.IsNullOrWhiteSpace(t.Notes) ? $"Ended: {reason.Trim()}" : $"{t.Notes} | Ended: {reason.Trim()}";
            Audit("term.ended", nameof(ParliamentaryTerm), t.Id, $"Ended parliamentary term #{t.Id} on {endDate:yyyy-MM-dd}: {reason.Trim()}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    // ------------------------------------------------------------------ party change

    /// <summary>
    /// Closes the person's open party period on the effective date and opens a new one. The earlier period keeps
    /// its party, so what the person belonged to in the past is never rewritten by a later change.
    /// </summary>
    public async Task<ServiceResult<int>> ChangePartyAsync(
        int personId, int? newPartyId, bool newIndependent, DateOnly effectiveFrom, string? sourceTitle, string? sourceUrl, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");
        if (newPartyId is null && !newIndependent) return Result.Fail<int>(ServiceFailure.Invalid, "party.choose", "Select the new party, or 'independent'.");
        if (newPartyId is not null && newIndependent) return Result.Fail<int>(ServiceFailure.Invalid, "party.conflict", "Choose a party or 'independent', not both.");
        if (!string.IsNullOrWhiteSpace(sourceUrl) && !AddPersonValidator.IsHttpUrl(sourceUrl))
            return Result.Fail<int>(ServiceFailure.Invalid, "party.source_url_invalid", "The source must be a web address starting with http:// or https://.");

        return await InTransactionAsync(async () =>
        {
            var person = await Db.People.FirstOrDefaultAsync(p => p.Id == personId, ct);
            if (person is null || person.IsArchived) return Missing<int>("That person");
            if (newPartyId is int pid && !await Db.PoliticalParties.AnyAsync(p => p.Id == pid && !p.IsArchived, ct))
                return Result.Fail<int>(ServiceFailure.Invalid, "party.missing", "The selected party does not exist.");

            var all = await Db.PartyAffiliations.Where(a => a.PersonId == personId && !a.IsArchived).ToListAsync(ct);
            var open = all.Where(a => a.EndDate is null).OrderByDescending(a => a.StartDate).FirstOrDefault();

            var report = new ValidationReport();
            if (open is not null)
            {
                if (effectiveFrom <= open.StartDate)
                    report.Error("party.before_current", $"The change must be after the current affiliation began ({open.StartDate:d MMM yyyy}).", "EffectiveFrom");
                if (open.PartyId == newPartyId)
                    report.Error("party.same", "The person is already recorded in that party.", "PartyId");
            }
            var closed = all.Where(a => a.Id != open?.Id).Select(a => new AffiliationPeriod(a.Id, a.PartyId, new DateRange(a.StartDate, a.EndDate))).ToList();
            if (open is not null) closed.Add(new AffiliationPeriod(open.Id, open.PartyId, new DateRange(open.StartDate, effectiveFrom)));
            report.AddRange(PartyAffiliationRules.Validate(new DateRange(effectiveFrom, null), closed));
            if (!report.IsValid) return Result.Invalid<int>(report);

            if (open is not null)
            {
                await SnapshotIfReviewedAsync(open, open.Stage, new { open.EndDate }, "Party changed", ct);
                open.EndDate = effectiveFrom;
            }

            var source = await ResolveSourceAsync(sourceTitle, sourceUrl, ct);
            var next = new PartyAffiliation { PersonId = personId, PartyId = newPartyId, StartDate = effectiveFrom, Source = source };
            Db.PartyAffiliations.Add(next);
            await Db.SaveChangesAsync(ct);

            Audit("party.changed", nameof(PartyAffiliation), next.Id, $"{person.FullName}: party change effective {effectiveFrom:yyyy-MM-dd}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(next.Id);
        }, ct);
    }

    // ------------------------------------------------------------------ administrations

    public async Task<ServiceResult<int>> CreateAdministrationAsync(string name, DateOnly start, int? electionCycleId, string? description, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");
        if (string.IsNullOrWhiteSpace(name)) return Result.Fail<int>(ServiceFailure.Invalid, "admin.name_required", "Enter the administration's name.");

        return await InTransactionAsync(async () =>
        {
            if (electionCycleId is int cid && !await Db.ElectionCycles.AnyAsync(c => c.Id == cid && !c.IsArchived, ct))
                return Result.Fail<int>(ServiceFailure.Invalid, "admin.cycle_missing", "The election cycle does not exist.");

            var a = new Administration { Name = name.Trim(), StartDate = start, ElectionCycleId = electionCycleId, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(), IsConfirmed = false };
            Db.Administrations.Add(a);
            await Db.SaveChangesAsync(ct);
            Audit("administration.created", nameof(Administration), a.Id, $"Created administration \"{a.Name}\" (unconfirmed)");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(a.Id);
        }, ct);
    }

    /// <summary>An administration is shown as historical fact only after someone with approval rights confirms it.</summary>
    public async Task<ServiceResult> ConfirmAdministrationAsync(int id, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsApprove)) return Denied("You do not have permission to confirm administrations.");
        var a = await Db.Administrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Missing("That administration");
        if (a.IsConfirmed) return Result.Ok();

        return await InTransactionAsync(async () =>
        {
            a.IsConfirmed = true;
            Audit("administration.confirmed", nameof(Administration), a.Id, $"Confirmed administration \"{a.Name}\"");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    /// <summary>
    /// Ends an administration. Open appointments belonging to IT are closed on the given date; MPs, the Speaker,
    /// appointments of other administrations and every person's other records are left exactly as they were.
    /// </summary>
    public async Task<ServiceResult<int>> CloseAdministrationAsync(int administrationId, DateOnly endDate, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsCorrect)) return Denied<int>("You do not have permission to close administrations.");

        return await InTransactionAsync(async () =>
        {
            var admin = await Db.Administrations.FirstOrDefaultAsync(a => a.Id == administrationId, ct);
            if (admin is null) return Missing<int>("That administration");
            if (admin.EndDate is not null) return Result.Fail<int>(ServiceFailure.Conflict, "admin.already_closed", "This administration already has an end date.");
            if (endDate < admin.StartDate) return Result.Fail<int>(ServiceFailure.Invalid, "admin.end_before_start", "The end date cannot be before the administration began.");

            var open = await Db.Appointments.Include(a => a.Position)
                .Where(a => a.AdministrationId == administrationId && a.EndDate == null && !a.IsArchived)
                .ToListAsync(ct);

            var (closures, report) = GovernmentChangePlanner.Plan(
                administrationId, endDate,
                open.Select(a => new OpenAppointment(a.Id, a.PersonId, a.AdministrationId, a.StartDate, a.Position.Title)));
            if (!report.IsValid) return Result.Invalid<int>(report);

            foreach (var c in closures)
            {
                var a = open.First(x => x.Id == c.RecordId);
                await SnapshotIfReviewedAsync(a, a.Stage, new { a.EndDate, a.Status, a.EndReason }, c.Reason, ct);
                CloseAppointment(a, c.EndDate, c.Reason);
            }
            admin.EndDate = endDate;

            Audit("administration.closed", nameof(Administration), admin.Id,
                $"Closed administration \"{admin.Name}\" on {endDate:yyyy-MM-dd}; ended {closures.Count} appointment(s)",
                new { closedAppointmentIds = closures.Select(c => c.RecordId).ToList() });
            await Db.SaveChangesAsync(ct);
            return Result.Ok(closures.Count, report);
        }, ct);
    }

    // ------------------------------------------------------------------ ministries

    /// <summary>Gives a ministry a new name from a date. The old name stays on record with the dates it applied.</summary>
    public async Task<ServiceResult> RenameMinistryAsync(int ministryId, string newName, DateOnly effectiveFrom, string? note, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsCorrect)) return Denied("You do not have permission to rename ministries.");
        if (string.IsNullOrWhiteSpace(newName)) return Result.Fail(ServiceFailure.Invalid, "ministry.name_required", "Enter the new name.");

        return await InTransactionAsync(async () =>
        {
            var m = await Db.Ministries.Include(x => x.Names).FirstOrDefaultAsync(x => x.Id == ministryId, ct);
            if (m is null) return Missing("That ministry");

            var name = newName.Trim();
            var current = m.Names.Where(n => n.ValidTo == null).OrderByDescending(n => n.ValidFrom).FirstOrDefault();
            if (current is null)
                return Result.Fail(ServiceFailure.Invalid, "ministry.no_current_era",
                    "Record the date the current name began first, so the old name can be kept with its dates.");
            if (effectiveFrom <= current.ValidFrom)
                return Result.Fail(ServiceFailure.Invalid, "ministry.date_before_current", $"The new name must start after the current name began ({current.ValidFrom:d MMM yyyy}).");
            if (string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                return Result.Fail(ServiceFailure.Invalid, "ministry.same_name", "That is already the ministry's name.");
            if (await Db.Ministries.AnyAsync(x => x.Id != ministryId && x.Name == name, ct))
                return Result.Fail(ServiceFailure.Conflict, "ministry.name_in_use", "Another ministry already uses that name.");

            current.ValidTo = effectiveFrom;
            m.Names.Add(new MinistryNameEra { Ministry = m, Name = name, ValidFrom = effectiveFrom, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
            var old = m.Name;
            m.Name = name;

            Audit("ministry.renamed", nameof(Ministry), m.Id, $"Ministry renamed \"{old}\" → \"{name}\" from {effectiveFrom:yyyy-MM-dd}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    /// <summary>Records the date the ministry's current name began (needed before the first rename).</summary>
    public async Task<ServiceResult> AddNameEraAsync(int ministryId, DateOnly validFrom, string? note, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied("You do not have permission to enter records.");
        return await InTransactionAsync(async () =>
        {
            var m = await Db.Ministries.Include(x => x.Names).FirstOrDefaultAsync(x => x.Id == ministryId, ct);
            if (m is null) return Missing("That ministry");
            if (m.Names.Any(n => n.ValidTo == null)) return Result.Fail(ServiceFailure.Conflict, "ministry.era_exists", "This ministry already has a current name period.");
            m.Names.Add(new MinistryNameEra { Ministry = m, Name = m.Name, ValidFrom = validFrom, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
            Audit("ministry.name_era_added", nameof(Ministry), m.Id, $"Recorded that \"{m.Name}\" has been the name since {validFrom:yyyy-MM-dd}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    // ------------------------------------------------------------------ archive

    public Task<ServiceResult> ArchiveAsync<T>(int id, string reason, CancellationToken ct = default) where T : AuditedEntity => SetArchivedAsync<T>(id, true, reason, ct);
    public Task<ServiceResult> RestoreAsync<T>(int id, string reason, CancellationToken ct = default) where T : AuditedEntity => SetArchivedAsync<T>(id, false, reason, ct);

    private async Task<ServiceResult> SetArchivedAsync<T>(int id, bool archive, string reason, CancellationToken ct) where T : AuditedEntity
    {
        if (!Can(Permissions.RecordsArchive)) return Denied("You do not have permission to archive records.");
        if (string.IsNullOrWhiteSpace(reason)) return Result.Fail(ServiceFailure.Invalid, "archive.reason_required", "Give a reason.");

        return await InTransactionAsync(async () =>
        {
            var e = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Missing("That record");
            if (e.IsArchived == archive) return Result.Ok();

            e.IsArchived = archive;
            e.ArchivedAtUtc = archive ? Now : null;
            e.ArchivedBy = archive ? User.UserId : null;
            Audit(archive ? "record.archived" : "record.restored", typeof(T).Name, id, $"{typeof(T).Name} #{id} {(archive ? "archived" : "restored")}: {reason.Trim()}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }

    // ------------------------------------------------------------------ helpers

    private static void CloseAppointment(Appointment a, DateOnly endDate, string reason)
    {
        a.EndDate = endDate;
        a.Status = AppointmentStatus.Ended;
        a.EndReason = reason;
    }

    /// <summary>Once a record has been reviewed, changing it leaves the previous values behind as a revision.</summary>
    private async Task SnapshotIfReviewedAsync(AuditedEntity e, WorkflowStage stage, object previous, string reason, CancellationToken ct)
    {
        if (stage >= WorkflowStage.Verified)
            await AddRevisionAsync(e.GetType().Name, e.Id, previous, reason, ct);
    }
}
