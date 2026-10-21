using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Service.Business.Export;

public enum ExportScope { Everything = 0, PublishedOnly = 1 }

public sealed record ExportCounts(int People, int Appointments, int Terms, int Affiliations, int Candidates, int ResultLines);
public sealed record ExportFile(byte[] Content, string FileName, ExportCounts Counts);

/// <summary>
/// Writes the directory to an Excel workbook, one sheet per kind of record. It only reads. Archived records are
/// left out. "Everything" includes drafts and records still in review (each row shows its Stage); "Published only"
/// is exactly what the public site shows. The export is recorded in the audit log.
/// </summary>
public sealed class ExportService:ServiceBase
{
    public ExportService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
       : base(db, user, clock, options) { }

    public async Task<ServiceResult<ExportFile>> BuildAsync(ExportScope scope, CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return Denied<ExportFile>("You do not have permission to export data.");
        var pub = scope == ExportScope.PublishedOnly;

        // ----- people (with the party they are recorded as holding today, if any)
        var people = await Db.People.AsNoTracking().Where(p => !p.IsArchived && (!pub || p.Stage == WorkflowStage.Published))
            .OrderBy(p => p.FullName).Select(p => new { p.Id, p.FullName, p.Stage, p.Biography, p.AdditionalInfo }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToHashSet();

        var affs = (await Db.PartyAffiliations.AsNoTracking().Where(a => !a.IsArchived && (!pub || a.Stage == WorkflowStage.Published))
            .Select(a => new { a.PersonId, Party = a.Party != null ? a.Party.Abbreviation : null, a.StartDate, a.EndDate, a.Stage }).ToListAsync(ct))
            .Where(a => ids.Contains(a.PersonId)).ToList();
        var apps = (await Db.Appointments.AsNoTracking().Where(a => !a.IsArchived && (!pub || a.Stage == WorkflowStage.Published))
            .Select(a => new
            {
                a.PersonId,
                Position = a.Position.Title,
                Admin = a.Administration != null ? a.Administration.Name : null,
                Ministry = a.Ministry != null ? a.Ministry.Name : null,
                a.StartDate,
                a.EndDate,
                a.Status,
                a.EndReason,
                Source = a.Source != null ? a.Source.Title : null,
                a.Stage
            }).ToListAsync(ct)).Where(a => ids.Contains(a.PersonId)).ToList();
        var terms = (await Db.ParliamentaryTerms.AsNoTracking().Where(t => !t.IsArchived && (!pub || t.Stage == WorkflowStage.Published))
            .Select(t => new
            {
                t.PersonId,
                Seat = t.Constituency.Name,
                Region = t.Constituency.Region.Name,
                Year = t.ElectionCycle != null ? (int?)t.ElectionCycle.Year : null,
                t.Kind,
                t.StartDate,
                t.EndDate,
                Source = t.Source != null ? t.Source.Title : null,
                t.Stage
            }).ToListAsync(ct)).Where(t => ids.Contains(t.PersonId)).ToList();

        var names = people.ToDictionary(p => p.Id, p => p.FullName);
        var today = Today;
        string CurrentParty(int id)
        {
            var current = affs.Where(a => a.PersonId == id && a.StartDate <= today && (a.EndDate is null || a.EndDate >= today))
                .OrderByDescending(a => a.StartDate).FirstOrDefault();
            return current is null ? "" : current.Party ?? "Independent";
        }

        var sPeople = new ExportSheet("People", new[] { "Person Id", "Full Name", "Stage", "Party Today", "Biography", "Additional Info" });
        foreach (var p in people) sPeople.Add(p.Id, p.FullName, p.Stage, CurrentParty(p.Id), p.Biography, p.AdditionalInfo);

        var sApps = new ExportSheet("Positions", new[] { "Person Id", "Full Name", "Position", "Administration", "Ministry", "Start Date", "End Date", "Status", "End Reason", "Source", "Stage" });
        foreach (var a in apps.OrderBy(a => names[a.PersonId]).ThenBy(a => a.StartDate))
            sApps.Add(a.PersonId, names[a.PersonId], a.Position, a.Admin, a.Ministry, a.StartDate, a.EndDate, a.Status, a.EndReason, a.Source, a.Stage);

        var sTerms = new ExportSheet("Parliament", new[] { "Person Id", "Full Name", "Constituency", "Region", "Election Year", "Kind", "Start Date", "End Date", "Source", "Stage" });
        foreach (var t in terms.OrderBy(t => names[t.PersonId]).ThenBy(t => t.StartDate))
            sTerms.Add(t.PersonId, names[t.PersonId], t.Seat, t.Region, t.Year, t.Kind, t.StartDate, t.EndDate, t.Source, t.Stage);

        var sAffs = new ExportSheet("Party History", new[] { "Person Id", "Full Name", "Party", "Start Date", "End Date", "Stage" });
        foreach (var a in affs.OrderBy(a => names[a.PersonId]).ThenBy(a => a.StartDate))
            sAffs.Add(a.PersonId, names[a.PersonId], a.Party ?? "Independent", a.StartDate, a.EndDate, a.Stage);

        // ----- candidates and results
        var cands = await Db.Candidates.AsNoTracking().Where(c => !c.IsArchived && (!pub || c.Stage == WorkflowStage.Published))
            .Select(c => new
            {
                c.Id,
                c.PersonId,
                Person = c.Person.FullName,
                Party = c.Party != null ? c.Party.Abbreviation : null,
                RunningMate = c.RunningMate != null ? c.RunningMate.FullName : null,
                c.BallotNumber,
                c.Status,
                c.Stage,
                Year = c.ElectionContest.ElectionCycle.Year,
                Cycle = c.ElectionContest.ElectionCycle.Name,
                c.ElectionContest.ContestType,
                c.ElectionContest.Round,
                Seat = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Name : null,
                Region = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Region.Name : null
            }).ToListAsync(ct);

        var sCands = new ExportSheet("Candidates", new[] { "Election", "Contest", "Constituency", "Region", "Round", "Person Id", "Full Name", "Party", "Running Mate", "Ballot No", "Status", "Stage" });
        foreach (var c in cands.OrderBy(c => c.Year).ThenBy(c => c.ContestType).ThenBy(c => c.Region).ThenBy(c => c.Seat).ThenBy(c => c.Person))
            sCands.Add(c.Cycle, c.ContestType, c.Seat, c.Region, c.ContestType == ContestType.Presidential ? c.Round : null, c.PersonId, c.Person,
                c.Party ?? "Independent", c.RunningMate, c.BallotNumber, c.Status, c.Stage);

        var lines = await Db.CandidateResults.AsNoTracking()
            .Where(l => !l.Candidate.IsArchived
                        && (!pub || (l.ElectionResult.Stage == WorkflowStage.Published && l.Candidate.Stage == WorkflowStage.Published))
                        && !l.ElectionResult.IsArchived)
            .Select(l => new
            {
                Cycle = l.ElectionResult.ElectionContest.ElectionCycle.Name,
                Year = l.ElectionResult.ElectionContest.ElectionCycle.Year,
                l.ElectionResult.ElectionContest.ContestType,
                l.ElectionResult.ElectionContest.Round,
                Seat = l.ElectionResult.ElectionContest.Constituency != null ? l.ElectionResult.ElectionContest.Constituency.Name : null,
                Region = l.ElectionResult.ElectionContest.Constituency != null ? l.ElectionResult.ElectionContest.Constituency.Region.Name : null,
                Person = l.Candidate.Person.FullName,
                Party = l.Candidate.Party != null ? l.Candidate.Party.Abbreviation : null,
                l.Votes,
                l.IsDeclaredWinner,
                l.ElectionResult.ValidVotes,
                l.ElectionResult.RejectedBallots,
                l.ElectionResult.TotalVotesCast,
                l.ElectionResult.RegisteredVoters,
                l.ElectionResult.DeclaredOn,
                l.ElectionResult.Basis,
                l.ElectionResult.Stage
            }).ToListAsync(ct);

        var sRes = new ExportSheet("Results", new[] { "Election", "Contest", "Constituency", "Region", "Round", "Candidate", "Party", "Votes", "Declared Winner", "Valid Votes", "Rejected Ballots", "Total Votes Cast", "Registered Voters", "Declared On", "Basis", "Stage" });
        foreach (var l in lines.OrderBy(l => l.Year).ThenBy(l => l.ContestType).ThenBy(l => l.Region).ThenBy(l => l.Seat).ThenByDescending(l => l.Votes ?? -1))
            sRes.Add(l.Cycle, l.ContestType, l.Seat, l.Region, l.ContestType == ContestType.Presidential ? l.Round : null, l.Person, l.Party ?? "Independent",
                l.Votes, l.IsDeclaredWinner, l.ValidVotes, l.RejectedBallots, l.TotalVotesCast, l.RegisteredVoters, l.DeclaredOn, l.Basis, l.Stage);

        var counts = new ExportCounts(people.Count, apps.Count, terms.Count, affs.Count, cands.Count, lines.Count);
        Audit("export.run", "Export", null, $"Exported the directory ({(pub ? "published only" : "everything")}): {counts.People} people, {counts.Appointments} positions, {counts.Candidates} candidates.", new { scope = scope.ToString(), counts });
        await Db.SaveChangesAsync(ct);

        var bytes = XlsxWriter.Write(new[] { sPeople, sApps, sTerms, sAffs, sCands, sRes });
        return Result.Ok(new ExportFile(bytes, $"ghana-directory-{(pub ? "published" : "all")}-{today:yyyyMMdd}.xlsx", counts));
    }

}

