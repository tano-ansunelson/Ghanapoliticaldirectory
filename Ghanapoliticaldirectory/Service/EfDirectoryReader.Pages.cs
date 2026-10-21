using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;
using NameEraModel = Ghanapoliticaldirectory.Models.MinistryNameEraDto;

namespace Ghanapoliticaldirectory.Services;

public sealed partial class EfDirectoryReader
{
    // ------------------------------------------------------------------ parliament directory

    public ParliamentPageModel GetParliament(ParliamentFilter filter)
    {
        var q = _db.ParliamentaryTerms.AsNoTracking()
            .Where(t => t.Stage == WorkflowStage.Published && !t.IsArchived && t.Person.Stage == WorkflowStage.Published && !t.Person.IsArchived);

        if (filter.Term is { Length: > 0 } && int.TryParse(filter.Term, out var termYear))
            q = q.Where(t => t.ElectionCycle != null && t.ElectionCycle.Year == termYear);
        if (filter.RegionId is not null) q = q.Where(t => t.Constituency.RegionId == filter.RegionId);
        if (filter.ConstituencyId is not null) q = q.Where(t => t.ConstituencyId == filter.ConstituencyId);
        if (filter.Name is { Length: > 0 })
        {
            var needle = filter.Name.Trim().ToLower();
            q = q.Where(t => t.Person.FullName.ToLower().Contains(needle) || t.Person.NormalizedName.Contains(needle));
        }

        var terms = q.Select(t => new
        {
            t.Id,
            t.PersonId,
            t.Person.FullName,
            t.Person.PhotoUrl,
            Region = t.Constituency.Region.Name,
            Constituency = t.Constituency.Name,
            t.StartDate,
            t.EndDate,
            Year = t.ElectionCycle != null ? (int?)t.ElectionCycle.Year : null,
            CandidateOk = t.Candidate != null && t.Candidate.Stage == WorkflowStage.Published && !t.Candidate.IsArchived,
            CandidateParty = t.Candidate != null ? t.Candidate.PartyId : null
        }).ToList();

        var people = terms.Select(t => t.PersonId).Distinct().ToList();
        var aff = LoadAffiliations(people);
        var today = Today;

        // other published offices held by the same people during the term
        var others = _db.Appointments.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && people.Contains(a.PersonId))
            .Select(a => new { a.PersonId, a.Position.Title, a.StartDate, a.EndDate }).ToList();

        var rows = terms.OrderByDescending(t => t.StartDate).Select(t =>
        {
            var party = t.CandidateOk ? RefOf(t.CandidateParty) : PartyAt(aff, t.PersonId, t.StartDate);
            var range = new DateRange(t.StartDate, t.EndDate);
            var other = string.Join("; ", others.Where(o => o.PersonId == t.PersonId && o.StartDate <= (t.EndDate ?? DateOnly.MaxValue) && (o.EndDate ?? DateOnly.MaxValue) >= t.StartDate)
                .Select(o => $"{o.Title} ({o.StartDate.Year}–{(o.EndDate is null ? "present" : o.EndDate.Value.Year.ToString())})"));
            var term = t.Year is int y ? $"Elected {y}" : "Other";
            return new MpRow(new PersonCardModel(t.PersonId, t.FullName, t.PhotoUrl, "Member of Parliament", party, UiHelpers.Range(t.StartDate, t.EndDate)),
                term, t.Region, t.Constituency, party, other.Length == 0 ? null : other);
        });

        // party filter: 0 = recorded as independent; unknown ("not recorded") matches no party
        if (filter.PartyId == 0) rows = rows.Where(r => r.PartyDuringTerm is null);
        else if (filter.PartyId is not null) rows = rows.Where(r => r.PartyDuringTerm?.Id == filter.PartyId);

        var ordered = rows.OrderBy(r => r.Region).ThenBy(r => r.Constituency).ToList();   // stable: newest term first within a seat
        var (page, info) = Page(ordered, filter.Page, 12);

        var termOptions = _db.ParliamentaryTerms.AsNoTracking()
            .Where(t => t.Stage == WorkflowStage.Published && !t.IsArchived && t.ElectionCycle != null)
            .Select(t => t.ElectionCycle!.Year).Distinct().ToList().OrderByDescending(y => y)
            .Select(y => new Option(y.ToString(), $"Elected in {y}")).ToList();

        return new ParliamentPageModel(
            filter, termOptions, RegionOptions(),
            filter.RegionId is null ? new List<Option>() : GetConstituencies(filter.RegionId.Value).ToList(),
            Parties.Values.OrderBy(p => p.Abbreviation).Select(p => new Option(p.Id.ToString(), $"{p.Name} ({p.Abbreviation})"))
                .Append(new Option("0", "Recorded as independent")).ToList(),
            page, info);
    }

    // ------------------------------------------------------------------ ministries and offices

    private AdministrationSummary? PickAdmin(int? administrationId) =>
        AdminSummaries.FirstOrDefault(a => a.Id == administrationId) ?? CurrentAdmin ?? AdminSummaries.FirstOrDefault();

    public MinistryPageModel GetMinistry(int? ministryId, int? administrationId)
    {
        var ministries = _db.Ministries.AsNoTracking().Where(m => !m.IsArchived).OrderBy(m => m.Name).ToList();
        var admin = PickAdmin(administrationId);
        var adminOptions = AdminSummaries.Select(a => new Option(a.Id.ToString(), a.Name + (a.IsCurrent ? " (current)" : ""))).ToList();

        var ministry = ministries.FirstOrDefault(m => m.Id == ministryId) ?? ministries.FirstOrDefault();
        if (ministry is null || admin is null)
            return new MinistryPageModel(
                ministries.Select(m => new Option(m.Id.ToString(), m.Name)).ToList(), adminOptions, ministry?.Id ?? 0, admin?.Id ?? 0,
                ministry?.Name ?? "", ministry?.Description ?? "", ministry?.WebsiteUrl, null, Array.Empty<OfficeholderRow>(),
                Array.Empty<OfficeholderRow>(), Array.Empty<NameEraModel>(), Array.Empty<OfficeholderRow>());

        var eras = _db.MinistryNameEras.AsNoTracking().Where(e => e.MinistryId == ministry.Id && !e.IsArchived).OrderBy(e => e.ValidFrom).ToList();
        var eraTuples = eras.Select(e => (e.Name, e.ValidFrom, e.ValidTo)).ToList();
        var allEras = MinistryEras(ministries.Select(m => m.Id));
        string NameIn(Ministry m, DateOnly on) => MinistryNameOn(m.Name, allEras.GetValueOrDefault(m.Id) ?? new(), on);

        var holders = _db.Appointments.AsNoTracking()
            .Where(a => a.MinistryId == ministry.Id && a.Stage == WorkflowStage.Published && !a.IsArchived
                        && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new
            {
                a.PersonId,
                a.Person.FullName,
                a.Person.PhotoUrl,
                Kind = a.Position.Kind,
                a.Position.Title,
                a.AdministrationId,
                AdminName = a.Administration != null ? a.Administration.Name : null,
                a.StartDate,
                a.EndDate
            }).ToList();
        var aff = LoadAffiliations(holders.Select(h => h.PersonId));

        OfficeholderRow ToRow(int personId, string fullName, string? photo, string title, DateOnly start, DateOnly? end, string role) =>
            new(new PersonCardModel(personId, fullName, photo, title, PartyAt(aff, personId, start)), role, start, end);

        var inAdmin = holders.Where(h => h.AdministrationId == admin.Id).OrderByDescending(h => h.StartDate).ToList();
        var minister = inAdmin.FirstOrDefault(h => h.Kind == PositionKind.Minister);
        var deputies = inAdmin.Where(h => h.Kind == PositionKind.DeputyMinister).OrderBy(h => h.FullName).ToList();
        var others = inAdmin.Where(h => h.Kind is not (PositionKind.Minister or PositionKind.DeputyMinister)).OrderBy(h => h.FullName).ToList();
        var previous = holders.Where(h => h.Kind == PositionKind.Minister && h.AdministrationId != admin.Id).OrderByDescending(h => h.StartDate).ToList();

        return new MinistryPageModel(
            ministries.Select(m => new Option(m.Id.ToString(), m.Name)).ToList(),
            adminOptions, ministry.Id, admin.Id,
            NameIn(ministry, admin.Start),               // the name the ministry had at the time, not today's
            ministry.Description ?? "", ministry.WebsiteUrl,
            minister is null ? null : ToRow(minister.PersonId, minister.FullName, minister.PhotoUrl, "Minister", minister.StartDate, minister.EndDate, "Minister"),
            deputies.Select(h => ToRow(h.PersonId, h.FullName, h.PhotoUrl, "Deputy Minister", h.StartDate, h.EndDate, "Deputy Minister")).ToList(),
            others.Select(h => ToRow(h.PersonId, h.FullName, h.PhotoUrl, h.Title, h.StartDate, h.EndDate, h.Title)).ToList(),
            eras.Select(e => new NameEraModel(e.Name, e.ValidFrom, e.ValidTo, e.Note)).ToList(),
            previous.Select(h => ToRow(h.PersonId, h.FullName, h.PhotoUrl, "Minister", h.StartDate, h.EndDate, $"Minister ({h.AdminName ?? "no administration recorded"})")).ToList());
    }

    public OfficesPageModel GetOffices(int? administrationId)
    {
        var admin = PickAdmin(administrationId);
        var options = AdminSummaries.Select(a => new Option(a.Id.ToString(), a.Name + (a.IsCurrent ? " (current)" : ""))).ToList();
        if (admin is null) return new OfficesPageModel(options, 0, Array.Empty<PublicOfficeModel>());

        var positions = _db.Positions.AsNoTracking()
            .Where(p => !p.IsArchived && !p.RequiresMinistry
                        && (p.Kind == PositionKind.ChiefOfStaff || p.Kind == PositionKind.Speaker || p.Kind == PositionKind.Other))
            .OrderBy(p => p.Title).ToList();
        var ids = positions.Select(p => p.Id).ToList();

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => ids.Contains(a.PositionId) && a.Stage == WorkflowStage.Published && !a.IsArchived
                        && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new { a.PositionId, a.PersonId, a.Person.FullName, a.Person.PhotoUrl, a.AdministrationId, a.StartDate, a.EndDate }).ToList();
        var aff = LoadAffiliations(appointments.Select(a => a.PersonId));

        var range = new DateRange(admin.Start, admin.End);
        var offices = positions.Select(p =>
        {
            var mine = appointments.Where(a => a.PositionId == p.Id).ToList();
            // an office tied to the administration uses its own record; an office that is not (the Speaker) is whoever held it as the administration began
            var holder = mine.Where(a => a.AdministrationId == admin.Id).OrderByDescending(a => a.StartDate).FirstOrDefault()
                         ?? (p.RequiresAdministration ? null
                             : mine.Where(a => a.AdministrationId == null && new DateRange(a.StartDate, a.EndDate).Overlaps(range)).OrderByDescending(a => a.StartDate).FirstOrDefault());
            var card = holder is null ? null : new PersonCardModel(holder.PersonId, holder.FullName, holder.PhotoUrl, p.Title, PartyAt(aff, holder.PersonId, holder.StartDate));
            return new PublicOfficeModel(p.Id, p.Title, p.Description ?? "", card, admin.Name);
        }).ToList();

        return new OfficesPageModel(options, admin.Id, offices);
    }

    // ------------------------------------------------------------------ archive

    public ArchivePageModel GetArchive(int? administrationId, int? compareWithId)
    {
        var all = AdminSummaries;
        var selected = PickAdmin(administrationId);
        if (selected is null)
            return new ArchivePageModel(all, null, null, Array.Empty<TimelineEvent>(), Array.Empty<SeatRow>(), null, Array.Empty<CompareRow>());

        var compare = compareWithId is not null && compareWithId != selected.Id ? all.FirstOrDefault(a => a.Id == compareWithId) : null;

        var cycleId = Admins.First(a => a.Summary.Id == selected.Id).ElectionCycleId;
        var cycle = cycleId is null ? null : _db.ElectionCycles.AsNoTracking().FirstOrDefault(c => c.Id == cycleId);

        var events = new List<TimelineEvent>();
        if (cycle is { ElectionDate: not null, IsDateConfirmed: true })
            events.Add(new TimelineEvent(cycle.ElectionDate.Value, $"{cycle.Year} general election held", "Election"));
        events.Add(new TimelineEvent(selected.Start, $"Administration begins · President {selected.President}", "Administration"));

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.AdministrationId == selected.Id && a.Stage == WorkflowStage.Published && !a.IsArchived
                        && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived
                        && a.Position.Kind != PositionKind.President)
            .Select(a => new { a.Person.FullName, a.Position.Title, a.StartDate, MinistryId = a.MinistryId, MinistryName = a.Ministry != null ? a.Ministry.Name : null })
            .OrderBy(a => a.StartDate).Take(60).ToList();
        var eras = MinistryEras(appointments.Where(a => a.MinistryId != null).Select(a => a.MinistryId!.Value));
        foreach (var a in appointments)
        {
            var title = a.MinistryId is int mid && a.MinistryName is not null
                ? $"{a.Title}, {MinistryNameOn(a.MinistryName, eras.GetValueOrDefault(mid) ?? new(), a.StartDate)}" : a.Title;
            events.Add(new TimelineEvent(a.StartDate, $"{title} · {a.FullName}", "Appointment"));
        }

        var renamed = _db.MinistryNameEras.AsNoTracking()
            .Where(e => !e.IsArchived && e.ValidFrom > selected.Start && (selected.End == null || e.ValidFrom <= selected.End) && e.Ministry.Names.Count > 1)
            .Select(e => new { e.Name, e.ValidFrom }).ToList();
        events.AddRange(renamed.Select(r => new TimelineEvent(r.ValidFrom, $"A ministry adopts the name “{r.Name}”", "Ministry change")));

        if (selected.End is not null)
            events.Add(new TimelineEvent(selected.End.Value, "Administration ends", "Administration"));

        var seatsA = Seats(selected);
        var seatsB = compare is null ? null : Seats(compare);

        CompareRow Row(string label, Func<AdministrationSummary, string> f) => new(label, f(selected), compare is null ? null : f(compare));
        var rows = new List<CompareRow>
        {
            Row("President", a => a.President),
            Row("Vice President", a => a.VicePresident),
            Row("Election cycle", a => a.ElectionYear?.ToString() ?? "Not recorded"),
            Row("Period", a => UiHelpers.Range(a.Start, a.End))
        };

        // who led each ministry in each administration (up to 10 ministries that have a published minister in either)
        var ids = new[] { selected.Id }.Concat(compare is null ? Array.Empty<int>() : new[] { compare.Id }).ToList();
        var ministers = _db.Appointments.AsNoTracking()
            .Where(a => a.Position.Kind == PositionKind.Minister && a.AdministrationId != null && ids.Contains(a.AdministrationId.Value)
                        && a.Stage == WorkflowStage.Published && !a.IsArchived && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new { a.AdministrationId, a.MinistryId, MinistryName = a.Ministry!.Name, a.Person.FullName, a.StartDate }).ToList();
        foreach (var m in ministers.GroupBy(m => m.MinistryId).OrderBy(g => g.First().MinistryName).Take(10))
        {
            string Who(AdministrationSummary a) => m.Where(x => x.AdministrationId == a.Id).OrderByDescending(x => x.StartDate).Select(x => x.FullName).FirstOrDefault() ?? "Not recorded";
            rows.Add(Row($"Minister · {m.First().MinistryName}", Who));
        }

        return new ArchivePageModel(all, selected, compare, events.OrderBy(e => e.On).ToList(), seatsA, seatsB, rows);
    }

    /// <summary>Seats by party on the day the administration began, from dated records. People with no recorded party are counted separately.</summary>
    private IReadOnlyList<SeatRow> Seats(AdministrationSummary admin)
    {
        var on = admin.Start;
        var terms = _db.ParliamentaryTerms.AsNoTracking()
            .Where(t => t.Stage == WorkflowStage.Published && !t.IsArchived && t.Person.Stage == WorkflowStage.Published && !t.Person.IsArchived
                        && t.StartDate <= on && (t.EndDate == null || t.EndDate >= on))
            .Select(t => new
            {
                t.PersonId,
                t.StartDate,
                CandidateOk = t.Candidate != null && t.Candidate.Stage == WorkflowStage.Published && !t.Candidate.IsArchived,
                CandidateParty = t.Candidate != null ? t.Candidate.PartyId : null
            }).ToList();
        if (terms.Count == 0) return Array.Empty<SeatRow>();

        var aff = LoadAffiliations(terms.Where(t => !t.CandidateOk).Select(t => t.PersonId));
        var counts = new Dictionary<string, int>();
        foreach (var t in terms)
        {
            var party = t.CandidateOk ? RefOf(t.CandidateParty) : PartyAt(aff, t.PersonId, t.StartDate);
            var key = party is null ? "Independent" : party.IsRecorded ? party.Abbreviation : "Not recorded";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts.OrderBy(c => c.Key).Select(c => new SeatRow(c.Key, c.Value)).ToList();
    }

    // ------------------------------------------------------------------ search

    public SearchPageModel Search(string? query)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2) return new SearchPageModel(q, Array.Empty<SearchResultItem>());
        var needle = q.ToLower();
        var items = new List<SearchResultItem>();

        var people = _db.People.AsNoTracking()
            .Where(p => p.Stage == WorkflowStage.Published && !p.IsArchived && (p.FullName.ToLower().Contains(needle) || p.NormalizedName.Contains(needle)))
            .OrderBy(p => p.FullName).Take(15).Select(p => new { p.Id, p.FullName }).ToList();
        var pid = people.Select(p => p.Id).ToList();
        var latest = _db.Appointments.AsNoTracking().Where(a => pid.Contains(a.PersonId) && a.Stage == WorkflowStage.Published && !a.IsArchived)
            .Select(a => new { a.PersonId, a.Position.Title, a.StartDate }).ToList();
        // no party in the subtitle: a list cannot show which date it refers to, and today's party must not stand in for the past
        items.AddRange(people.Select(p => new SearchResultItem(p.FullName, "Person",
            latest.Where(l => l.PersonId == p.Id).OrderByDescending(l => l.StartDate).Select(l => l.Title).FirstOrDefault(), $"/people/{p.Id}")));

        items.AddRange(Parties.Values.Where(p => p.Name.ToLower().Contains(needle) || p.Abbreviation.ToLower().Contains(needle))
            .Select(p => new SearchResultItem($"{p.Name} ({p.Abbreviation})", "Party", null, $"/parties/{p.Id}")));

        // ministries match on any name they have ever had
        var ministries = _db.Ministries.AsNoTracking()
            .Where(m => !m.IsArchived && (m.Name.ToLower().Contains(needle) || m.Names.Any(n => n.Name.ToLower().Contains(needle))))
            .OrderBy(m => m.Name).Take(10).ToList();
        items.AddRange(ministries.Select(m => new SearchResultItem(m.Name, "Ministry", null, $"/ministries?ministryId={m.Id}")));

        var regions = _db.Regions.AsNoTracking().Where(r => !r.IsArchived && r.Name.ToLower().Contains(needle)).OrderBy(r => r.Name).ToList();
        items.AddRange(regions.Select(r => new SearchResultItem($"{r.Name} Region", "Region", null, $"/elections/parliamentary?regionId={r.Id}")));

        var seats = _db.Constituencies.AsNoTracking().Where(c => !c.IsArchived && c.Name.ToLower().Contains(needle))
            .OrderBy(c => c.Name).Take(10).Select(c => new { c.Id, c.Name, c.RegionId, Region = c.Region.Name }).ToList();
        items.AddRange(seats.Select(c => new SearchResultItem(c.Name, "Constituency", $"{c.Region} Region", $"/elections/parliamentary?regionId={c.RegionId}&constituencyId={c.Id}")));

        var positions = _db.Positions.AsNoTracking().Where(p => !p.IsArchived && p.Title.ToLower().Contains(needle)).OrderBy(p => p.Title).Take(8).ToList();
        items.AddRange(positions.Select(p => new SearchResultItem(p.Title, "Position", null, "/parliament")));

        items.AddRange(Cycles.Where(c => c.Name.ToLower().Contains(needle) || c.Year.ToString().Contains(needle))
            .Select(c => new SearchResultItem(c.Name, "Election year", null, $"/elections/presidential?year={c.Year}")));
        items.AddRange(AdminSummaries.Where(a => a.Name.ToLower().Contains(needle))
            .Select(a => new SearchResultItem(a.Name, "Administration", null, $"/archive?administrationId={a.Id}")));

        return new SearchPageModel(q, items.Take(40).ToList());
    }
}
