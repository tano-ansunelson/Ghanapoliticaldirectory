
using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

public sealed record PartyRow(int Id, string Name, string Abbreviation, DateOnly? FoundedOn, string? Description);
public sealed record AffiliationRow(int Id, string Party, DateOnly Start, DateOnly? End, WorkflowStage Stage);
public sealed record AppointmentAdminRow(int Id, string Title, string? Ministry, string? Administration, DateOnly Start, DateOnly? End, AppointmentStatus Status, WorkflowStage Stage);
public sealed record TermAdminRow(int Id, string Constituency, string Region, DateOnly Start, DateOnly? End, WorkflowStage Stage, string? Administration = null);
public sealed record CandidacyRow(int Id, string Contest, string Party, CandidateStatus Status, WorkflowStage Stage);
public sealed record PersonEditView(int Id, string FullName, string? Biography, string? AdditionalInfo, WorkflowStage Stage, bool IsArchived, string? PhotoUrl, string? PhotoAlt);
public sealed record PersonAdminView(
    int Id, string FullName, WorkflowStage Stage, bool IsArchived,
    IReadOnlyList<AffiliationRow> Affiliations, IReadOnlyList<AppointmentAdminRow> Appointments,
    IReadOnlyList<TermAdminRow> Terms, IReadOnlyList<CandidacyRow> Candidacies);
public sealed record ListedAppointment(int Id, int PersonId, string Person, string Title, string? Ministry, string? Administration, DateOnly Start, DateOnly? End, WorkflowStage Stage);
public sealed record ListedTerm(int Id, int PersonId, string Person, string Constituency, string Region, DateOnly Start, DateOnly? End, WorkflowStage Stage, string? Administration = null);
public sealed record ContestRow(int Id, string Cycle, string Label, ContestType Type, int Round, int Candidates, WorkflowStage? ResultStage);
public sealed record CandidateListRow(int Id, int PersonId, string Name, string Party, string? RunningMate, WorkflowStage Stage);
public sealed record ContestInfo(int? ContestId, string Label, IReadOnlyList<CandidateListRow> Candidates, WorkflowStage? ResultStage);
public sealed record ConstituencyOption(int Id, string Name, string Region);

/// <summary>Read-only lists for the admin screens. Nothing here changes data.</summary>
public sealed class AdminQueries : ServiceBase
{
    public AdminQueries(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    public async Task<IReadOnlyList<PartyRow>> PartiesAsync(CancellationToken ct = default) =>
        (await Db.PoliticalParties.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Abbreviation).ToListAsync(ct))
            .Select(p => new PartyRow(p.Id, p.Name, p.Abbreviation, p.FoundedOn, p.Description)).ToList();

    public async Task<IReadOnlyList<(int Id, string Name)>> PeopleOptionsAsync(CancellationToken ct = default) =>
        (await Db.People.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.FullName).Select(p => new { p.Id, p.FullName }).ToListAsync(ct))
            .Select(p => (p.Id, p.FullName)).ToList();

    public async Task<IReadOnlyList<ConstituencyOption>> ConstituenciesAsync(CancellationToken ct = default) =>
        (await Db.Constituencies.AsNoTracking().Where(c => !c.IsArchived).Select(c => new { c.Id, c.Name, Region = c.Region.Name }).ToListAsync(ct))
            .OrderBy(c => c.Region).ThenBy(c => c.Name).Select(c => new ConstituencyOption(c.Id, c.Name, c.Region)).ToList();

    public async Task<PersonEditView?> PersonForEditAsync(int id, CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return null;
        var p = await Db.People.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : new PersonEditView(p.Id, p.FullName, p.Biography, p.AdditionalInfo, p.Stage, p.IsArchived, p.PhotoUrl, p.PhotoAltText);
    }

    public async Task<PersonAdminView?> PersonAsync(int id, CancellationToken ct = default)
    {
        var p = await Db.People.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        var aff = await Db.PartyAffiliations.AsNoTracking().Where(a => a.PersonId == id && !a.IsArchived)
            .Select(a => new { a.Id, Party = a.Party != null ? a.Party.Abbreviation : null, a.StartDate, a.EndDate, a.Stage }).ToListAsync(ct);
        var apps = await Db.Appointments.AsNoTracking().Where(a => a.PersonId == id && !a.IsArchived)
            .Select(a => new { a.Id, a.Position.Title, Ministry = a.Ministry != null ? a.Ministry.Name : null, Admin = a.Administration != null ? a.Administration.Name : null, a.StartDate, a.EndDate, a.Status, a.Stage }).ToListAsync(ct);
        var terms = await Db.ParliamentaryTerms.AsNoTracking().Where(t => t.PersonId == id && !t.IsArchived)
            .Select(t => new { t.Id, Seat = t.Constituency.Name, Region = t.Constituency.Region.Name, t.StartDate, t.EndDate, t.Stage, Admin = t.Administration != null ? t.Administration.Name : null }).ToListAsync(ct);
        var cands = await Db.Candidates.AsNoTracking().Where(c => c.PersonId == id && !c.IsArchived)
            .Select(c => new {
                c.Id,
                Cycle = c.ElectionContest.ElectionCycle.Name,
                Seat = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Name : null,
                Party = c.Party != null ? c.Party.Abbreviation : null,
                c.Status,
                c.Stage
            }).ToListAsync(ct);

        return new PersonAdminView(p.Id, p.FullName, p.Stage, p.IsArchived,
            aff.OrderBy(a => a.StartDate).Select(a => new AffiliationRow(a.Id, a.Party ?? "Independent / no party", a.StartDate, a.EndDate, a.Stage)).ToList(),
            apps.OrderBy(a => a.StartDate).Select(a => new AppointmentAdminRow(a.Id, a.Title, a.Ministry, a.Admin, a.StartDate, a.EndDate, a.Status, a.Stage)).ToList(),
            terms.OrderBy(t => t.StartDate).Select(t => new TermAdminRow(t.Id, t.Seat, t.Region, t.StartDate, t.EndDate, t.Stage, t.Admin)).ToList(),
            cands.Select(c => new CandidacyRow(c.Id, c.Cycle + (c.Seat is null ? " · Presidential" : " · " + c.Seat), c.Party ?? "Independent", c.Status, c.Stage)).ToList());
    }

    public async Task<IReadOnlyList<ListedAppointment>> AppointmentsAsync(string? q, int take = 300, CancellationToken ct = default)
    {
        var query = Db.Appointments.AsNoTracking().Where(a => !a.IsArchived);
        if (!string.IsNullOrWhiteSpace(q)) { var n = q.Trim().ToLower(); query = query.Where(a => a.Person.FullName.ToLower().Contains(n) || a.Position.Title.ToLower().Contains(n)); }
        var rows = await query.OrderByDescending(a => a.StartDate).Take(take)
            .Select(a => new {
                a.Id,
                a.PersonId,
                Person = a.Person.FullName,
                a.Position.Title,
                Ministry = a.Ministry != null ? a.Ministry.Name : null,
                Admin = a.Administration != null ? a.Administration.Name : null,
                a.StartDate,
                a.EndDate,
                a.Stage
            }).ToListAsync(ct);
        return rows.Select(a => new ListedAppointment(a.Id, a.PersonId, a.Person, a.Title, a.Ministry, a.Admin, a.StartDate, a.EndDate, a.Stage)).ToList();
    }

    public async Task<IReadOnlyList<ListedTerm>> TermsAsync(string? q, int take = 300, CancellationToken ct = default)
    {
        var query = Db.ParliamentaryTerms.AsNoTracking().Where(t => !t.IsArchived);
        if (!string.IsNullOrWhiteSpace(q)) { var n = q.Trim().ToLower(); query = query.Where(t => t.Person.FullName.ToLower().Contains(n) || t.Constituency.Name.ToLower().Contains(n)); }
        var rows = await query.OrderByDescending(t => t.StartDate).Take(take)
            .Select(t => new { t.Id, t.PersonId, Person = t.Person.FullName, Seat = t.Constituency.Name, Region = t.Constituency.Region.Name, t.StartDate, t.EndDate, t.Stage, Admin = t.Administration != null ? t.Administration.Name : null }).ToListAsync(ct);
        return rows.Select(t => new ListedTerm(t.Id, t.PersonId, t.Person, t.Seat, t.Region, t.StartDate, t.EndDate, t.Stage, t.Admin)).ToList();
    }

    /// <summary>Contests that already have candidates, with where their result stands.</summary>
    /// <summary>The contest being worked on and the candidates it already has. A contest that does not exist yet comes back empty.</summary>
    public async Task<ContestInfo?> ContestInfoAsync(int cycleId, ContestType type, int? constituencyId, int round, CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return null;
        var cycle = await Db.ElectionCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cycleId && !c.IsArchived, ct);
        if (cycle is null) return null;

        string label;
        if (type == ContestType.Presidential) label = $"{cycle.Name}: Presidential{(round > 1 ? $" (round {round})" : "")}";
        else
        {
            var seat = await Db.Constituencies.AsNoTracking().Where(c => c.Id == constituencyId).Select(c => new { c.Name, Region = c.Region.Name }).FirstOrDefaultAsync(ct);
            if (seat is null) return null;
            label = $"{cycle.Name}: {seat.Name}, {seat.Region}";
        }

        var contest = await Db.ElectionContests.AsNoTracking().FirstOrDefaultAsync(c => c.ElectionCycleId == cycleId && c.ContestType == type
            && c.ConstituencyId == constituencyId && c.Round == round && !c.IsArchived, ct);
        if (contest is null) return new ContestInfo(null, label, Array.Empty<CandidateListRow>(), null);

        var rows = (await Db.Candidates.AsNoTracking().Where(c => c.ElectionContestId == contest.Id && !c.IsArchived)
            .Select(c => new {
                c.Id,
                c.PersonId,
                Name = c.Person.FullName,
                Party = c.Party != null ? c.Party.Abbreviation : "Independent",
                Mate = c.RunningMate != null ? c.RunningMate.FullName : null,
                c.Stage
            }).ToListAsync(ct))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CandidateListRow(c.Id, c.PersonId, c.Name, c.Party, c.Mate, c.Stage)).ToList();
        var resultStage = await Db.ElectionResults.AsNoTracking().Where(x => x.ElectionContestId == contest.Id).Select(x => (WorkflowStage?)x.Stage).FirstOrDefaultAsync(ct);
        return new ContestInfo(contest.Id, label, rows, resultStage);
    }

    public async Task<IReadOnlyList<ContestRow>> ContestsAsync(int? cycleId, CancellationToken ct = default)
    {
        var query = Db.ElectionContests.AsNoTracking().Where(c => !c.IsArchived);
        if (cycleId is not null) query = query.Where(c => c.ElectionCycleId == cycleId);
        var rows = await query.Select(c => new
        {
            c.Id,
            Cycle = c.ElectionCycle.Name,
            c.ElectionCycle.Year,
            c.ContestType,
            c.Round,
            Seat = c.Constituency != null ? c.Constituency.Name : null,
            Region = c.Constituency != null ? c.Constituency.Region.Name : null,
            Candidates = c.Candidates.Count(x => !x.IsArchived),
            ResultStage = c.Result != null ? (WorkflowStage?)c.Result.Stage : null
        }).ToListAsync(ct);
        return rows.OrderByDescending(r => r.Year).ThenBy(r => r.ContestType).ThenBy(r => r.Region).ThenBy(r => r.Seat)
            .Select(r => new ContestRow(r.Id, r.Cycle,
                r.Seat is null ? $"Presidential{(r.Round > 1 ? $" (round {r.Round})" : "")}" : $"{r.Seat}, {r.Region}",
                r.ContestType, r.Round, r.Candidates, r.ResultStage)).ToList();
    }
}
