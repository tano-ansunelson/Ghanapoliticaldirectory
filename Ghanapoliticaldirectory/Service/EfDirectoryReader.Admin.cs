using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Services;

/// <summary>The administration side of the reader: counts, lists and drop-down options. Writing goes through the services.</summary>
public sealed partial class EfDirectoryReader
{
    public const string IndependentOption = "independent";

    public AdminDashboardModel GetAdminDashboard()
    {
        var cards = new[]
        {
            new SummaryCard("Total people", _db.People.Count(p => !p.IsArchived), "bi-people", "People records (one per person)", "/admin/people"),
            new SummaryCard("Political parties", _db.PoliticalParties.Count(p => !p.IsArchived), "bi-flag", null, null),
            new SummaryCard("Election cycles", _db.ElectionCycles.Count(c => !c.IsArchived), "bi-calendar-event", "Including upcoming", null),
            new SummaryCard("Constituencies", _db.Constituencies.Count(c => !c.IsArchived), "bi-geo-alt", null, null),
            new SummaryCard("Ministries", _db.Ministries.Count(m => !m.IsArchived), "bi-briefcase", null, "/admin/ministries"),
            new SummaryCard("Administrations", _db.Administrations.Count(a => !a.IsArchived), "bi-building", null, "/admin/administrations"),
            new SummaryCard("Published results", _db.ElectionResults.Count(r => !r.IsArchived && r.Stage == WorkflowStage.Published), "bi-check2-circle", "Verified and published", "/admin/results"),
            new SummaryCard("Awaiting review", AwaitingCount(), "bi-hourglass-split", "Needs a reviewer", "/admin/review")
        };

        var users = _db.Users.AsNoTracking().Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName }).ToList().ToDictionary(u => u.Id, u => u.Name);
        string Who(string? id) => id is not null && users.TryGetValue(id, out var n) ? n ?? id : id ?? "unknown";

        var awaiting = new List<AwaitingItem>();
        var stages = new[] { WorkflowStage.Draft, WorkflowStage.AwaitingVerification, WorkflowStage.Verified, WorkflowStage.Approved };
        awaiting.AddRange(_db.People.AsNoTracking().Where(p => !p.IsArchived && stages.Contains(p.Stage)).OrderByDescending(p => p.UpdatedAtUtc ?? p.CreatedAtUtc).Take(6)
            .Select(p => new { p.FullName, p.CreatedBy, p.Stage, At = p.UpdatedAtUtc ?? p.CreatedAtUtc }).ToList()
            .Select(p => new AwaitingItem(p.FullName, "Person", Who(p.CreatedBy), DateOnly.FromDateTime(p.At), StageName(p.Stage))));
        awaiting.AddRange(_db.ElectionResults.AsNoTracking().Where(r => !r.IsArchived && stages.Contains(r.Stage)).OrderByDescending(r => r.UpdatedAtUtc ?? r.CreatedAtUtc).Take(6)
            .Select(r => new { Label = r.ElectionContest.ElectionCycle.Name + (r.ElectionContest.Constituency != null ? " · " + r.ElectionContest.Constituency.Name : " · Presidential"), r.CreatedBy, r.Stage, At = r.UpdatedAtUtc ?? r.CreatedAtUtc }).ToList()
            .Select(r => new AwaitingItem(r.Label, "Election result", Who(r.CreatedBy), DateOnly.FromDateTime(r.At), StageName(r.Stage))));
        awaiting.AddRange(_db.Appointments.AsNoTracking().Where(a => !a.IsArchived && stages.Contains(a.Stage)).OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc).Take(6)
            .Select(a => new { Label = a.Person.FullName + " · " + a.Position.Title, a.CreatedBy, a.Stage, At = a.UpdatedAtUtc ?? a.CreatedAtUtc }).ToList()
            .Select(a => new AwaitingItem(a.Label, "Appointment", Who(a.CreatedBy), DateOnly.FromDateTime(a.At), StageName(a.Stage))));

        var audit = _db.AuditLogs.AsNoTracking().OrderByDescending(a => a.AtUtc).Take(8).ToList()
            .Select(a => new AuditEntry(a.AtUtc, a.UserName, a.Action, a.Summary)).ToList();

        return new AdminDashboardModel(cards, awaiting.OrderByDescending(a => a.Date).Take(8).ToList(), audit);
    }

    private int AwaitingCount()
    {
        var waiting = new[] { WorkflowStage.AwaitingVerification, WorkflowStage.Verified, WorkflowStage.Approved };
        return _db.People.Count(p => !p.IsArchived && waiting.Contains(p.Stage))
             + _db.Appointments.Count(a => !a.IsArchived && waiting.Contains(a.Stage))
             + _db.ParliamentaryTerms.Count(t => !t.IsArchived && waiting.Contains(t.Stage))
             + _db.ElectionResults.Count(r => !r.IsArchived && waiting.Contains(r.Stage));
    }

    public static string StageName(WorkflowStage s) => s switch
    {
        WorkflowStage.Draft => "Draft",
        WorkflowStage.AwaitingVerification => "Awaiting verification",
        WorkflowStage.Verified => "Verified",
        WorkflowStage.Approved => "Approved",
        _ => "Published"
    };

    public AdminPeopleModel GetAdminPeople(string? q, string? status, int page)
    {
        var people = _db.People.AsNoTracking().AsQueryable();

        if (status == "Archived") people = people.Where(p => p.IsArchived);
        else
        {
            people = people.Where(p => !p.IsArchived);
            if (!string.IsNullOrWhiteSpace(status))
            {
                var stages = status switch
                {
                    "Published" => new[] { WorkflowStage.Published },
                    "Awaiting verification" => new[] { WorkflowStage.AwaitingVerification, WorkflowStage.Verified, WorkflowStage.Approved },
                    "Draft" => new[] { WorkflowStage.Draft },
                    _ => Array.Empty<WorkflowStage>()
                };
                people = people.Where(p => stages.Contains(p.Stage));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            people = people.Where(p => p.FullName.ToLower().Contains(needle) || p.NormalizedName.Contains(needle)
                || p.Appointments.Any(a => a.Position.Title.ToLower().Contains(needle))
                || p.Affiliations.Any(a => a.Party != null && (a.Party.Abbreviation.ToLower().Contains(needle) || a.Party.Name.ToLower().Contains(needle))));
        }

        var total = people.Count();
        var info = new PagedInfo(Math.Max(1, page), 8, total);
        var pageNo = Math.Min(info.Page, info.TotalPages);
        info = info with { Page = pageNo };

        var rows = people.OrderBy(p => p.FullName).Skip((pageNo - 1) * 8).Take(8)
            .Select(p => new { p.Id, p.FullName, p.Stage, p.IsArchived, At = p.UpdatedAtUtc ?? p.CreatedAtUtc }).ToList();
        var ids = rows.Select(r => r.Id).ToList();

        var positions = _db.Appointments.AsNoTracking().Where(a => ids.Contains(a.PersonId) && !a.IsArchived).Select(a => new { a.PersonId, a.Position.Kind, a.Position.Title }).ToList();
        var terms = _db.ParliamentaryTerms.AsNoTracking().Where(t => ids.Contains(t.PersonId) && !t.IsArchived).Select(t => t.PersonId).ToList();
        var candidacies = _db.Candidates.AsNoTracking().Where(c => ids.Contains(c.PersonId) && !c.IsArchived).Select(c => c.PersonId).Distinct().ToList();
        var affiliations = _db.PartyAffiliations.AsNoTracking().Where(a => ids.Contains(a.PersonId) && !a.IsArchived)
            .Select(a => new { a.PersonId, a.StartDate, a.EndDate, Party = a.Party != null ? a.Party.Abbreviation : null }).ToList();

        var result = rows.Select(r =>
        {
            var held = positions.Where(p => p.PersonId == r.Id).Select(p => p.Kind == PositionKind.Other ? p.Title : PositionLabel(p.Kind)).ToList();
            if (terms.Contains(r.Id)) held.Add("MP");
            var label = held.Count > 0 ? string.Join(", ", held.Distinct()) : candidacies.Contains(r.Id) ? "Candidate" : "No position recorded";
            var latest = affiliations.Where(a => a.PersonId == r.Id).OrderByDescending(a => a.StartDate).FirstOrDefault();
            var party = latest is null ? "Not recorded" : latest.Party ?? "Independent";
            return new AdminPersonRow(r.Id, r.FullName, label, party, r.IsArchived ? "Archived" : r.Stage == WorkflowStage.Published ? "Published"
                : r.Stage == WorkflowStage.Draft ? "Draft" : "Awaiting verification", DateOnly.FromDateTime(r.At));
        }).ToList();

        return new AdminPeopleModel(q, status, result, info);
    }

    public IReadOnlyList<SimilarPerson> FindSimilarPeople(string name)
    {
        var people = _db.People.AsNoTracking().Select(p => new { p.Id, p.FullName, p.IsArchived, p.Stage }).ToList();
        var matches = DuplicateDetector.Find(name, people.Select(p => (p.Id, p.FullName)));
        return matches.Select(m =>
        {
            var p = people.First(x => x.Id == m.PersonId);
            var how = m.Level == DuplicateLevel.Likely ? "Likely the same person" : "Possibly the same person";
            return new SimilarPerson(m.PersonId, m.FullName, $"{how} · {StageName(p.Stage)}{(p.IsArchived ? " · archived" : "")} · profile #{m.PersonId}");
        }).ToList();
    }

    public AdminFormOptions GetFormOptions() => new(
        Parties.Values.OrderBy(p => p.Abbreviation).Select(p => new Option(p.Id.ToString(), $"{p.Name} ({p.Abbreviation})"))
            .Append(new Option(IndependentOption, "Independent / no party")).ToList(),
        GetElectionCycleOptions(),
        _db.Administrations.AsNoTracking().Where(a => !a.IsArchived).OrderByDescending(a => a.StartDate).ToList()
            .Select(a => new Option(a.Id.ToString(), a.Name)).ToList(),
        _db.Ministries.AsNoTracking().Where(m => !m.IsArchived).OrderBy(m => m.Name).ToList().Select(m => new Option(m.Id.ToString(), m.Name)).ToList(),
        RegionOptions(),
        new[] { "Active", "Acting", "Ended", "Unconfirmed" }.Select(s => new Option(s, s)).ToList());

    public IReadOnlyList<Option> GetElectionCycleOptions() =>
        _db.ElectionCycles.AsNoTracking().Where(c => !c.IsArchived).OrderByDescending(c => c.Year).ToList().Select(c => new Option(c.Id.ToString(), c.Name)).ToList();
}
