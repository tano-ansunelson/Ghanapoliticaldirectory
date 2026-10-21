using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;
using NameEraModel = Ghanapoliticaldirectory.Models.MinistryNameEra;

namespace Ghanapoliticaldirectory.Services;

/// <summary>
/// Reads the public site from the database. Rules that matter here:
///  * only PUBLISHED, non-archived records are ever shown, and a record is hidden if the person it belongs to is;
///  * a person's party is always the party recorded FOR THE DATE in question. With no record for that date the party
///    is "not recorded", never today's party and never "independent";
///  * ministry names are the names in force on the date being shown;
///  * percentages and winners follow ResultLabelPolicy: nothing is calculated from provisional figures.
/// </summary>
public sealed partial class EfDirectoryReader : IDirectoryReader
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public EfDirectoryReader(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow);

    // ------------------------------------------------------------------ shared helpers

    private Dictionary<int, PartyRef>? _parties;
    private Dictionary<int, PartyRef> Parties => _parties ??= _db.PoliticalParties.AsNoTracking()
        .Where(p => !p.IsArchived).OrderBy(p => p.Abbreviation).ToList()
        .ToDictionary(p => p.Id, p => new PartyRef(p.Id, p.Abbreviation, p.Name));

    private PartyRef? RefOf(int? partyId) =>
        partyId is null ? null : Parties.TryGetValue(partyId.Value, out var r) ? r : PartyRef.NotRecorded;

    private static (IReadOnlyList<T> Items, PagedInfo Info) Page<T>(IEnumerable<T> source, int page, int size)
    {
        var list = source.ToList();
        var info = new PagedInfo(Math.Max(1, page), size, list.Count);
        var p = Math.Min(info.Page, info.TotalPages);
        info = info with { Page = p };
        return (list.Skip((p - 1) * size).Take(size).ToList(), info);
    }

    private static ResultStatus StatusOf(ResultLabel? label) => label switch
    {
        ResultLabel.Provisional => ResultStatus.Provisional,
        ResultLabel.Verified => ResultStatus.Verified,
        ResultLabel.OfficiallyDeclared => ResultStatus.OfficiallyDeclared,
        ResultLabel.Corrected => ResultStatus.Corrected,
        _ => ResultStatus.NotPublished
    };

    private static string Trim(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Length <= max ? text : text[..max].TrimEnd() + "…";

    private static DateOnly? DateOf(DateTime? utc) => utc is null ? null : DateOnly.FromDateTime(utc.Value);

    /// <summary>Published party periods for the given people.</summary>
    private Dictionary<int, List<AffiliationPeriod>> LoadAffiliations(IEnumerable<int> personIds)
    {
        var ids = personIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        return _db.PartyAffiliations.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && ids.Contains(a.PersonId))
            .Select(a => new { a.PersonId, a.Id, a.PartyId, a.StartDate, a.EndDate })
            .ToList()
            .GroupBy(a => a.PersonId)
            .ToDictionary(g => g.Key, g => g.Select(a => new AffiliationPeriod(a.Id, a.PartyId, new DateRange(a.StartDate, a.EndDate))).ToList());
    }

    /// <summary>The party on a date: null = recorded as independent; NotRecorded = nothing recorded for that date.</summary>
    private PartyRef? PartyAt(Dictionary<int, List<AffiliationPeriod>> all, int personId, DateOnly date)
    {
        var periods = all.TryGetValue(personId, out var l) ? l : new List<AffiliationPeriod>();
        var on = PartyAffiliationRules.PartyAt(date, periods);
        return !on.IsKnown ? PartyRef.NotRecorded : RefOf(on.PartyId);
    }

    private static string PositionLabel(PositionKind kind) => kind switch
    {
        PositionKind.President => "President",
        PositionKind.VicePresident => "Vice President",
        PositionKind.MemberOfParliament => "Member of Parliament",
        PositionKind.Minister => "Minister",
        PositionKind.DeputyMinister => "Deputy Minister",
        PositionKind.ChiefOfStaff => "Chief of Staff",
        PositionKind.Speaker => "Speaker of Parliament",
        _ => "Other public office"
    };

    /// <summary>The name a ministry had on a date, from its dated name periods; falls back to the current name.</summary>
    private static string MinistryNameOn(string currentName, IEnumerable<(string Name, DateOnly From, DateOnly? To)> eras, DateOnly date)
    {
        var era = eras.Where(e => e.From <= date && (e.To is null || e.To >= date)).OrderByDescending(e => e.From).FirstOrDefault();
        return era.Name ?? currentName;
    }

    public bool HasSampleData() =>
        _db.People.AsNoTracking().Any(p => p.IsSample && p.Stage == WorkflowStage.Published && !p.IsArchived);

    public StaticPageModel? GetStaticPage(string slug) => StaticPages.Get(slug);

    // ------------------------------------------------------------------ election cycles

    private List<ElectionCycleSummary>? _cycles;
    private List<ElectionCycleSummary> Cycles => _cycles ??= BuildCycles();

    private static ElectionCycleSummary NoCycle(DateOnly today) =>
        new(0, today.Year, "No election has been published yet", ElectionPhase.Upcoming, false, ResultStatus.NotPublished, ResultStatus.NotPublished);

    private List<ElectionCycleSummary> BuildCycles()
    {
        var cycles = _db.ElectionCycles.AsNoTracking()
            .Where(c => c.Stage == WorkflowStage.Published && !c.IsArchived)
            .OrderByDescending(c => c.Year).ThenBy(c => c.Name).ToList();

        var results = _db.ElectionResults.AsNoTracking()
            .Where(r => r.Stage == WorkflowStage.Published && !r.IsArchived)
            .Select(r => new { r.ElectionContest.ElectionCycleId, r.ElectionContest.ContestType, r.Basis, r.HasBeenCorrected, r.Stage })
            .ToList();

        var withCandidates = _db.Candidates.AsNoTracking()
            .Where(c => c.Stage == WorkflowStage.Published && !c.IsArchived)
            .Select(c => c.ElectionContest.ElectionCycleId).Distinct().ToList();

        ResultStatus Overall(int cycleId, ContestType type)
        {
            var labels = results.Where(r => r.ElectionCycleId == cycleId && r.ContestType == type)
                .Select(r => ResultLabelPolicy.PublicLabel(r.Stage, r.Basis, r.HasBeenCorrected)).Where(l => l is not null).Select(l => l!.Value).ToList();
            return StatusOf(ResultLabelPolicy.Weakest(labels));
        }

        return cycles.Select(c =>
        {
            var any = results.Any(r => r.ElectionCycleId == c.Id);
            var phase = any ? ElectionPhase.Completed : withCandidates.Contains(c.Id) ? ElectionPhase.CandidatesPublished : ElectionPhase.Upcoming;
            return new ElectionCycleSummary(c.Id, c.Year, c.Name, phase, withCandidates.Contains(c.Id),
                Overall(c.Id, ContestType.Presidential), Overall(c.Id, ContestType.Parliamentary));
        }).ToList();
    }

    private ElectionCycleSummary PickCycle(int? year) =>
        Cycles.FirstOrDefault(c => c.Year == year) ?? Cycles.FirstOrDefault() ?? NoCycle(Today);

    // ------------------------------------------------------------------ administrations

    private sealed record AdminInfo(AdministrationSummary Summary, int? ElectionCycleId);

    private List<AdminInfo>? _admins;

    /// <summary>Confirmed, published administrations, newest first, with the President and Vice President recorded for each.</summary>
    private List<AdminInfo> Admins => _admins ??= BuildAdmins();

    private List<AdminInfo> BuildAdmins()
    {
        var admins = _db.Administrations.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && a.IsConfirmed)
            .OrderByDescending(a => a.StartDate).ToList();
        if (admins.Count == 0) return new();

        var ids = admins.Select(a => a.Id).ToList();
        var leaders = _db.Appointments.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && a.AdministrationId != null && ids.Contains(a.AdministrationId.Value)
                        && (a.Position.Kind == PositionKind.President || a.Position.Kind == PositionKind.VicePresident)
                        && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new { a.AdministrationId, a.Position.Kind, a.Person.FullName, a.StartDate })
            .ToList();
        var cycleYears = _db.ElectionCycles.AsNoTracking().Select(c => new { c.Id, c.Year }).ToList().ToDictionary(c => c.Id, c => c.Year);

        string Leader(int adminId, PositionKind kind) =>
            leaders.Where(l => l.AdministrationId == adminId && l.Kind == kind).OrderByDescending(l => l.StartDate).Select(l => l.FullName).FirstOrDefault()
            ?? "Not recorded";

        return admins.Select(a => new AdminInfo(new AdministrationSummary(
            a.Id, a.Name, a.StartDate, a.EndDate,
            Leader(a.Id, PositionKind.President), Leader(a.Id, PositionKind.VicePresident),
            a.ElectionCycleId is int cid && cycleYears.TryGetValue(cid, out var y) ? y : null,
            IsCurrent: a.EndDate is null || a.EndDate >= Today,
            IsDraft: false), a.ElectionCycleId)).ToList();
    }

    private AdministrationSummary? CurrentAdmin =>
        Admins.Select(a => a.Summary).Where(a => a.IsCurrent && a.Start <= Today).OrderByDescending(a => a.Start).FirstOrDefault();

    private IReadOnlyList<AdministrationSummary> AdminSummaries => Admins.Select(a => a.Summary).ToList();

    // ------------------------------------------------------------------ home, presidency

    public HomeViewModel GetHome()
    {
        var current = CurrentAdmin;
        var cards = new List<PersonCardModel>();

        if (current is not null)
        {
            var today = Today;
            var rows = _db.Appointments.AsNoTracking()
                .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && a.AdministrationId == current.Id
                            && (a.EndDate == null || a.EndDate >= today)
                            && (a.IsFeatured || a.Position.Kind == PositionKind.President || a.Position.Kind == PositionKind.VicePresident)
                            && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
                .Select(a => new
                {
                    a.PersonId,
                    a.Person.FullName,
                    a.Person.PhotoUrl,
                    Kind = a.Position.Kind,
                    a.Position.Title,
                    a.MinistryId,
                    MinistryName = a.Ministry != null ? a.Ministry.Name : null,
                    a.StartDate
                }).ToList();

            var aff = LoadAffiliations(rows.Select(r => r.PersonId));
            var eraSets = MinistryEras(rows.Where(r => r.MinistryId != null).Select(r => r.MinistryId!.Value));

            cards = rows
                .OrderBy(r => r.Kind == PositionKind.President ? 0 : r.Kind == PositionKind.VicePresident ? 1 : 2)
                .ThenBy(r => r.Title).ThenBy(r => r.FullName)
                .Select(r =>
                {
                    var title = r.MinistryId is int mid && r.MinistryName is not null
                        ? $"{r.Title}, {MinistryNameOn(r.MinistryName, eraSets.GetValueOrDefault(mid) ?? new(), today)}"
                        : r.Title;
                    return new PersonCardModel(r.PersonId, r.FullName, r.PhotoUrl, title, PartyAt(aff, r.PersonId, today));
                }).ToList();
        }

        var cycles = Cycles;
        return new HomeViewModel(cycles, cycles.FirstOrDefault() ?? NoCycle(Today), current, cards, AdminSummaries);
    }

    public IReadOnlyList<PresidencyRow> GetPresidency()
    {
        var admins = Admins;
        if (admins.Count == 0) return Array.Empty<PresidencyRow>();

        var ids = admins.Select(a => a.Summary.Id).ToList();
        var leaders = _db.Appointments.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && a.AdministrationId != null && ids.Contains(a.AdministrationId.Value)
                        && (a.Position.Kind == PositionKind.President || a.Position.Kind == PositionKind.VicePresident)
                        && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new { a.AdministrationId, a.Position.Kind, a.PersonId, a.Person.FullName, a.Person.PhotoUrl, a.StartDate })
            .ToList();
        var aff = LoadAffiliations(leaders.Select(l => l.PersonId));

        PersonCardModel? Card(int adminId, PositionKind kind)
        {
            var l = leaders.Where(x => x.AdministrationId == adminId && x.Kind == kind).OrderByDescending(x => x.StartDate).FirstOrDefault();
            return l is null ? null : new PersonCardModel(l.PersonId, l.FullName, l.PhotoUrl, PositionLabel(kind), PartyAt(aff, l.PersonId, l.StartDate));
        }

        return admins.Select(a => new PresidencyRow(a.Summary, Card(a.Summary.Id, PositionKind.President), Card(a.Summary.Id, PositionKind.VicePresident))).ToList();
    }

    // ------------------------------------------------------------------ ministries (names by date)

    private Dictionary<int, List<(string Name, DateOnly From, DateOnly? To)>> MinistryEras(IEnumerable<int> ministryIds)
    {
        var ids = ministryIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        return _db.MinistryNameEras.AsNoTracking().Where(e => ids.Contains(e.MinistryId) && !e.IsArchived)
            .Select(e => new { e.MinistryId, e.Name, e.ValidFrom, e.ValidTo }).ToList()
            .GroupBy(e => e.MinistryId)
            .ToDictionary(g => g.Key, g => g.Select(e => (e.Name, e.ValidFrom, e.ValidTo)).ToList());
    }

    // ------------------------------------------------------------------ person profile

    public PersonProfileModel? GetPerson(int id)
    {
        var person = _db.People.AsNoTracking().FirstOrDefault(p => p.Id == id && p.Stage == WorkflowStage.Published && !p.IsArchived);
        if (person is null) return null;

        var affiliationRows = _db.PartyAffiliations.AsNoTracking()
            .Where(a => a.PersonId == id && a.Stage == WorkflowStage.Published && !a.IsArchived)
            .OrderBy(a => a.StartDate).ToList();
        var affiliations = affiliationRows.Select(a => new Models.PartyAffiliationDto(RefOf(a.PartyId), a.StartDate, a.EndDate)).ToList();
        var periods = LoadAffiliations(new[] { id });

        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.PersonId == id && a.Stage == WorkflowStage.Published && !a.IsArchived)
            .Select(a => new
            {
                a.Id,
                a.Position.Kind,
                a.Position.Title,
                a.MinistryId,
                MinistryName = a.Ministry != null ? a.Ministry.Name : null,
                AdminId = a.AdministrationId,
                AdminName = a.Administration != null ? a.Administration.Name : null,
                a.StartDate,
                a.EndDate,
                a.Status,
                SourceTitle = a.Source != null ? a.Source.Title : null,
                SourceUrl = a.Source != null ? a.Source.Url : null,
                SourcePublished = a.Source != null ? a.Source.PublishedOn : null,
                SourceChecked = a.Source != null ? a.Source.LastCheckedOn : null,
                SourceStatus = a.Source != null ? a.Source.VerificationStatus : SourceVerification.Unverified
            }).ToList();

        var terms = _db.ParliamentaryTerms.AsNoTracking()
            .Where(t => t.PersonId == id && t.Stage == WorkflowStage.Published && !t.IsArchived)
            .Select(t => new
            {
                t.Id,
                t.StartDate,
                t.EndDate,
                t.ConstituencyId,
                Constituency = t.Constituency.Name,
                RegionId = t.Constituency.RegionId,
                Region = t.Constituency.Region.Name,
                CandidateOk = t.Candidate != null && t.Candidate.Stage == WorkflowStage.Published && !t.Candidate.IsArchived,
                CandidateParty = t.Candidate != null ? t.Candidate.PartyId : null,
                SourceTitle = t.Source != null ? t.Source.Title : null,
                SourceUrl = t.Source != null ? t.Source.Url : null,
                SourcePublished = t.Source != null ? t.Source.PublishedOn : null,
                SourceChecked = t.Source != null ? t.Source.LastCheckedOn : null,
                SourceStatus = t.Source != null ? t.Source.VerificationStatus : SourceVerification.Unverified
            }).ToList();

        var eras = MinistryEras(appointments.Where(a => a.MinistryId != null).Select(a => a.MinistryId!.Value));
        var today = Today;

        SourceRef? Src(string? title, string? url, DateOnly? published, DateOnly? checkedOn, SourceVerification status) =>
            title is null ? null : new SourceRef(title, url, published, checkedOn, status switch
            {
                SourceVerification.Verified => "Verified against source",
                SourceVerification.Disputed => "Disputed",
                _ => "Not yet verified"
            });

        var records = new List<ServiceRecord>();
        foreach (var a in appointments)
        {
            var detail = a.MinistryId is int mid && a.MinistryName is not null
                ? MinistryNameOn(a.MinistryName, eras.GetValueOrDefault(mid) ?? new(), a.StartDate) : null;
            var link = a.MinistryId is int m2
                ? new Link(detail ?? "Ministry", $"/ministries?ministryId={m2}" + (a.AdminId is int ad ? $"&administrationId={ad}" : ""))
                : a.Kind is PositionKind.President or PositionKind.VicePresident ? new Link("Presidency", "/presidency") : null;
            var ended = a.EndDate is not null && a.EndDate < today;
            records.Add(new ServiceRecord(PositionLabel(a.Kind), a.Title, detail, a.AdminName, a.StartDate, a.EndDate,
                ended ? "Ended" : a.Status.ToString(), PartyAt(periods, id, a.StartDate), link,
                Src(a.SourceTitle, a.SourceUrl, a.SourcePublished, a.SourceChecked, a.SourceStatus)));
        }
        foreach (var t in terms)
        {
            var party = t.CandidateOk ? RefOf(t.CandidateParty) : PartyAt(periods, id, t.StartDate);
            var ended = t.EndDate is not null && t.EndDate < today;
            records.Add(new ServiceRecord("Member of Parliament", "Member of Parliament", $"{t.Constituency} · {t.Region} Region", null,
                t.StartDate, t.EndDate, ended ? "Ended" : "Active", party,
                new Link("Parliament directory", $"/parliament?regionId={t.RegionId}&constituencyId={t.ConstituencyId}"),
                Src(t.SourceTitle, t.SourceUrl, t.SourcePublished, t.SourceChecked, t.SourceStatus)));
        }

        // election history: only published candidacies, labelled by what has actually been published about the contest
        var candidacies = _db.Candidates.AsNoTracking()
            .Where(c => c.PersonId == id && c.Stage == WorkflowStage.Published && !c.IsArchived)
            .Select(c => new
            {
                c.Id,
                c.PartyId,
                c.ElectionContestId,
                c.ElectionContest.ContestType,
                Year = c.ElectionContest.ElectionCycle.Year,
                Constituency = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Name : null,
                ConstituencyId = c.ElectionContest.ConstituencyId,
                RegionId = c.ElectionContest.Constituency != null ? (int?)c.ElectionContest.Constituency.RegionId : null,
                CycleOk = c.ElectionContest.ElectionCycle.Stage == WorkflowStage.Published && !c.ElectionContest.ElectionCycle.IsArchived
            }).ToList().Where(c => c.CycleOk).ToList();

        var contestIds = candidacies.Select(c => c.ElectionContestId).ToList();
        var contestResults = _db.ElectionResults.AsNoTracking().Include(r => r.Lines)
            .Where(r => contestIds.Contains(r.ElectionContestId) && r.Stage == WorkflowStage.Published && !r.IsArchived).ToList();

        var history = new List<ElectionHistoryItem>();
        foreach (var c in candidacies.OrderByDescending(c => c.Year))
        {
            var result = contestResults.FirstOrDefault(r => r.ElectionContestId == c.ElectionContestId);
            var label = result is null ? null : ResultLabelPolicy.PublicLabel(result.Stage, result.Basis, result.HasBeenCorrected);
            var flagged = result?.Lines.Any(l => l.CandidateId == c.Id && l.IsDeclaredWinner) ?? false;
            var outcome = result is not null && ResultLabelPolicy.ShowWinner(label, result.Basis, flagged) ? "Declared winner (Electoral Commission)" : null;
            history.Add(new ElectionHistoryItem(c.Year, c.ContestType == ContestType.Presidential ? "Presidential" : "Parliamentary",
                c.Constituency, RefOf(c.PartyId), StatusOf(label), outcome));
        }

        // sources: those attached to the person plus those attached to their records
        var personSources = _db.PersonSources.AsNoTracking().Where(ps => ps.PersonId == id)
            .Select(ps => ps.Source).ToList()
            .Select(s => Src(s.Title, s.Url, s.PublishedOn, s.LastCheckedOn, s.VerificationStatus)!).ToList();
        var sources = personSources
            .Concat(records.Where(r => r.Source is not null).Select(r => r.Source!))
            .GroupBy(s => (s.Title, s.Url)).Select(g => g.First()).ToList();

        var appearsOn = records.Where(r => r.RelatedPage is not null).Select(r => r.RelatedPage!).GroupBy(l => l.Url).Select(g => g.First()).ToList();

        return new PersonProfileModel(person.Id, person.FullName, person.PhotoUrl, person.Biography ?? "",
            affiliations, records.OrderByDescending(r => r.Start).ToList(), history, sources, appearsOn);
    }

    // ------------------------------------------------------------------ parties

    public IReadOnlyList<PartyRef> GetParties() => Parties.Values.OrderBy(p => p.Abbreviation).ToList();

    private sealed record PartyRow(PartyPersonRow Row, int? AdministrationId, int? RegionId);

    public PartyPageModel? GetParty(int partyId, PartyFilter filter)
    {
        var party = _db.PoliticalParties.AsNoTracking().FirstOrDefault(p => p.Id == partyId && !p.IsArchived);
        if (party is null) return null;
        var partyRef = new PartyRef(party.Id, party.Abbreviation, party.Name);
        var admins = Admins;
        var rows = new List<PartyRow>();

        string AdminNameOn(DateOnly date) =>
            admins.Select(a => a.Summary).Where(a => a.Start <= date && (a.End is null || a.End >= date)).OrderByDescending(a => a.Start).Select(a => a.Name).FirstOrDefault() ?? "—";
        int? AdminIdOn(DateOnly date) =>
            admins.Select(a => a.Summary).Where(a => a.Start <= date && (a.End is null || a.End >= date)).OrderByDescending(a => a.Start).Select(a => (int?)a.Id).FirstOrDefault();

        // 1. members of parliament: the party at the election if a published candidacy exists, else the dated affiliation
        var terms = _db.ParliamentaryTerms.AsNoTracking()
            .Where(t => t.Stage == WorkflowStage.Published && !t.IsArchived && t.Person.Stage == WorkflowStage.Published && !t.Person.IsArchived)
            .Select(t => new
            {
                t.PersonId,
                t.Person.FullName,
                t.Person.PhotoUrl,
                Constituency = t.Constituency.Name,
                t.Constituency.RegionId,
                Region = t.Constituency.Region.Name,
                t.StartDate,
                t.EndDate,
                Year = t.ElectionCycle != null ? (int?)t.ElectionCycle.Year : null,
                CandidateOk = t.Candidate != null && t.Candidate.Stage == WorkflowStage.Published && !t.Candidate.IsArchived,
                CandidateParty = t.Candidate != null ? t.Candidate.PartyId : null
            }).ToList();

        // 2. appointments: the party on the day the appointment began
        var appointments = _db.Appointments.AsNoTracking()
            .Where(a => a.Stage == WorkflowStage.Published && !a.IsArchived && a.Person.Stage == WorkflowStage.Published && !a.Person.IsArchived)
            .Select(a => new
            {
                a.PersonId,
                a.Person.FullName,
                a.Person.PhotoUrl,
                a.Position.Title,
                a.AdministrationId,
                AdminName = a.Administration != null ? a.Administration.Name : null,
                AdminYear = a.Administration != null && a.Administration.ElectionCycle != null ? (int?)a.Administration.ElectionCycle.Year : null,
                a.StartDate,
                a.EndDate
            }).ToList();

        var aff = LoadAffiliations(terms.Where(t => !t.CandidateOk).Select(t => t.PersonId).Concat(appointments.Select(a => a.PersonId)));

        foreach (var t in terms)
        {
            var atTime = t.CandidateOk ? RefOf(t.CandidateParty) : PartyAt(aff, t.PersonId, t.StartDate);
            if (atTime?.Id != partyId) continue;
            var card = new PersonCardModel(t.PersonId, t.FullName, t.PhotoUrl, "Member of Parliament", atTime, AdminNameOn(t.StartDate));
            rows.Add(new PartyRow(new PartyPersonRow(card, "Member of Parliament", AdminNameOn(t.StartDate), t.Region, t.Constituency, t.Year, t.StartDate, t.EndDate, atTime),
                AdminIdOn(t.StartDate), t.RegionId));
        }

        foreach (var a in appointments)
        {
            var atTime = PartyAt(aff, a.PersonId, a.StartDate);
            if (atTime?.Id != partyId) continue;
            var card = new PersonCardModel(a.PersonId, a.FullName, a.PhotoUrl, a.Title, atTime, a.AdminName);
            rows.Add(new PartyRow(new PartyPersonRow(card, a.Title, a.AdminName ?? "—", null, null, a.AdminYear, a.StartDate, a.EndDate, atTime), a.AdministrationId, null));
        }

        // 3. candidates: the party the person stood for in that contest
        var candidates = _db.Candidates.AsNoTracking()
            .Where(c => c.PartyId == partyId && c.Stage == WorkflowStage.Published && !c.IsArchived
                        && c.Person.Stage == WorkflowStage.Published && !c.Person.IsArchived
                        && c.ElectionContest.ElectionCycle.Stage == WorkflowStage.Published)
            .Select(c => new
            {
                c.PersonId,
                c.Person.FullName,
                c.Person.PhotoUrl,
                c.ElectionContest.ContestType,
                Year = c.ElectionContest.ElectionCycle.Year,
                Date = c.ElectionContest.ElectionCycle.ElectionDate,
                Constituency = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Name : null,
                RegionId = c.ElectionContest.Constituency != null ? (int?)c.ElectionContest.Constituency.RegionId : null,
                Region = c.ElectionContest.Constituency != null ? c.ElectionContest.Constituency.Region.Name : null
            }).ToList();

        foreach (var c in candidates)
        {
            var position = c.ContestType == ContestType.Presidential ? "Presidential candidate" : "Parliamentary candidate";
            var card = new PersonCardModel(c.PersonId, c.FullName, c.PhotoUrl, position, partyRef, $"{c.Year} general election");
            rows.Add(new PartyRow(new PartyPersonRow(card, position, "—", c.Region, c.Constituency, c.Year, c.Date, null, partyRef), null, c.RegionId));
        }

        IEnumerable<PartyRow> q = rows;
        if (filter.Year is not null) q = q.Where(r => r.Row.ElectionYear == filter.Year);
        if (filter.Position is { Length: > 0 }) q = q.Where(r => r.Row.Position == filter.Position);
        if (filter.RegionId is not null) q = q.Where(r => r.RegionId == filter.RegionId);
        if (filter.AdministrationId is not null) q = q.Where(r => r.AdministrationId == filter.AdministrationId);

        var (page, info) = Page(q.Select(r => r.Row).OrderBy(r => r.Person.FullName).ThenByDescending(r => r.Start), filter.Page, 8);

        // presidential candidate of this party in each published election
        var presidential = _db.Candidates.AsNoTracking()
            .Where(c => c.PartyId == partyId && c.ElectionContest.ContestType == ContestType.Presidential && c.Stage == WorkflowStage.Published && !c.IsArchived
                        && c.Person.Stage == WorkflowStage.Published && !c.Person.IsArchived)
            .Select(c => new { c.ElectionContest.ElectionCycleId, c.Person.FullName }).ToList();
        var history = Cycles.Select(c => new PartyElectionRow(c.Year,
            presidential.Where(p => p.ElectionCycleId == c.Id).Select(p => p.FullName).FirstOrDefault() ?? "No candidate recorded",
            c.PresidentialStatus)).ToList();

        var regions = _db.Regions.AsNoTracking().Where(r => !r.IsArchived).OrderBy(r => r.Name).ToList();
        return new PartyPageModel(
            partyRef, party.Description ?? "", party.WebsiteUrl, filter, GetParties(),
            Cycles.Select(c => new Option(c.Year.ToString(), c.Year.ToString())).ToList(),
            admins.Select(a => new Option(a.Summary.Id.ToString(), a.Summary.Name)).ToList(),
            new[] { "Member of Parliament", "Minister", "Deputy Minister", "Parliamentary candidate", "Presidential candidate" }.Select(p => new Option(p, p)).ToList(),
            regions.Select(r => new Option(r.Id.ToString(), r.Name)).ToList(),
            page, info, history);
    }

    // ------------------------------------------------------------------ elections

    private sealed record ContestView(IReadOnlyList<CandidateModel> Candidates, ContestResult? Result);

    private ContestView LoadContest(int cycleId, ContestType type, int? constituencyId, ElectionCycleSummary cycle)
    {
        if (cycleId == 0) return new ContestView(Array.Empty<CandidateModel>(), null);

        var contests = _db.ElectionContests.AsNoTracking()
            .Where(c => c.ElectionCycleId == cycleId && c.ContestType == type && c.ConstituencyId == constituencyId && !c.IsArchived).ToList();
        if (contests.Count == 0) return new ContestView(Array.Empty<CandidateModel>(), null);

        var ids = contests.Select(c => c.Id).ToList();
        var results = _db.ElectionResults.AsNoTracking().Include(r => r.Lines).Include(r => r.Sources).ThenInclude(s => s.Source)
            .Where(r => ids.Contains(r.ElectionContestId) && r.Stage == WorkflowStage.Published && !r.IsArchived).ToList();

        // a run-off supersedes the first round once it has a published result
        var contest = contests.Where(c => results.Any(r => r.ElectionContestId == c.Id)).OrderByDescending(c => c.Round).FirstOrDefault()
                      ?? contests.OrderBy(c => c.Round).First();
        var result = results.FirstOrDefault(r => r.ElectionContestId == contest.Id);
        var label = result is null ? null : ResultLabelPolicy.PublicLabel(result.Stage, result.Basis, result.HasBeenCorrected);

        var rows = _db.Candidates.AsNoTracking()
            .Where(c => c.ElectionContestId == contest.Id && c.Stage == WorkflowStage.Published && !c.IsArchived
                        && c.Person.Stage == WorkflowStage.Published && !c.Person.IsArchived)
            .Select(c => new
            {
                c.Id,
                c.PersonId,
                c.Person.FullName,
                c.Person.PhotoUrl,
                c.Person.Biography,
                c.Status,
                c.PartyId,
                RunningMate = c.RunningMate != null && c.RunningMate.Stage == WorkflowStage.Published && !c.RunningMate.IsArchived ? c.RunningMate.FullName : null
            }).ToList();

        var lines = result?.Lines.ToDictionary(l => l.CandidateId) ?? new();
        var denominator = result is null ? null
            : ResultMath.Denominator(result.ValidVotes, result.Lines.Select(l => new ResultLineInput(l.CandidateId.ToString(), "", l.Votes, l.IsDeclaredWinner)).ToList());
        var showPct = result is not null && ResultLabelPolicy.ShowPercentages(label, result.Basis);

        var candidates = rows.Select(r =>
        {
            lines.TryGetValue(r.Id, out var line);
            return new CandidateModel(
                r.PersonId, r.FullName, r.PhotoUrl, RefOf(r.PartyId),
                r.Status switch
                {
                    CandidateStatus.Withdrawn => "Withdrawn",
                    CandidateStatus.Disqualified => "Disqualified",
                    CandidateStatus.Contested when cycle.Phase == ElectionPhase.Completed => "Contested",
                    _ => "Candidate"
                },
                r.RunningMate, Trim(r.Biography, 280),
                line?.Votes,
                showPct && line is not null ? ResultMath.Percentage(line.Votes, denominator) : null,
                result is not null && line is not null && ResultLabelPolicy.ShowWinner(label, result.Basis, line.IsDeclaredWinner));
        }).OrderBy(c => c.FullName).ToList();

        ContestResult? view = null;
        if (result is not null)
        {
            var revisions = _db.RecordRevisions.AsNoTracking()
                .Where(r => r.EntityType == nameof(ElectionResult) && r.EntityId == result.Id).OrderBy(r => r.ChangedAtUtc).ToList();
            var changes = new List<ChangeLogItem>();
            if (result.PublishedAtUtc is not null)
                changes.Add(new ChangeLogItem(DateOf(result.PublishedAtUtc)!.Value, "Result published."));
            changes.AddRange(revisions.Select(r => new ChangeLogItem(DateOnly.FromDateTime(r.ChangedAtUtc), "Corrected: " + r.Reason + " The earlier figures are kept in the change history.")));

            view = new ContestResult(
                StatusOf(label),
                new ResultTotals(result.ValidVotes, result.RejectedBallots, result.TotalVotesCast, result.RegisteredVoters),
                result.Sources.Select(s => new SourceRef(s.Source.Title, s.Source.Url, s.Source.PublishedOn, s.Source.LastCheckedOn,
                    s.Source.VerificationStatus switch { SourceVerification.Verified => "Verified against source", SourceVerification.Disputed => "Disputed", _ => "Not yet verified" })).ToList(),
                DateOf(result.VerifiedAtUtc), DateOf(result.PublishedAtUtc),
                result.HasBeenCorrected ? result.CorrectionNote : null,
                changes);
        }
        return new ContestView(candidates, view);
    }

    public PresidentialPageModel GetPresidential(int? year)
    {
        var cycle = PickCycle(year);
        var c = LoadContest(cycle.Id, ContestType.Presidential, null, cycle);
        return new PresidentialPageModel(Cycles, cycle, c.Candidates, c.Result);
    }

    public IReadOnlyList<Option> GetConstituencies(int regionId) =>
        _db.Constituencies.AsNoTracking().Where(c => c.RegionId == regionId && !c.IsArchived).OrderBy(c => c.Name).ToList()
            .Select(c => new Option(c.Id.ToString(), c.Name)).ToList();

    private List<Option> RegionOptions() =>
        _db.Regions.AsNoTracking().Where(r => !r.IsArchived).OrderBy(r => r.Name).ToList().Select(r => new Option(r.Id.ToString(), r.Name)).ToList();

    public ParliamentaryPageModel GetParliamentary(int? year, int? regionId, int? constituencyId)
    {
        var cycle = PickCycle(year);
        var regions = RegionOptions();
        var constituencies = regionId is null ? new List<Option>() : GetConstituencies(regionId.Value).ToList();
        var regionName = regionId is null ? null : _db.Regions.AsNoTracking().Where(r => r.Id == regionId).Select(r => r.Name).FirstOrDefault();

        var seat = constituencyId is null ? null
            : _db.Constituencies.AsNoTracking().FirstOrDefault(c => c.Id == constituencyId && c.RegionId == regionId && !c.IsArchived);
        if (seat is null)
            return new ParliamentaryPageModel(Cycles, cycle, regions, constituencies, regionId, null, regionName, null, Array.Empty<CandidateModel>(), null);

        var contest = LoadContest(cycle.Id, ContestType.Parliamentary, seat.Id, cycle);
        return new ParliamentaryPageModel(Cycles, cycle, regions, constituencies, regionId, seat.Id, regionName, seat.Name, contest.Candidates, contest.Result);
    }
}
