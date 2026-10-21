namespace Ghanapoliticaldirectory.Models;

// ---------------------------------------------------------------------------------
// Presentation models (view models) for the public site.
// They are deliberately NOT EF entities: Phase 2 maps database entities onto these.
// ---------------------------------------------------------------------------------

public enum ResultStatus
{
    NotPublished = 0,
    Provisional,
    Verified,
    OfficiallyDeclared,
    Corrected,
    Published
}

public enum ElectionPhase { Upcoming, CandidatesPublished, Completed }

public record Option(string Value, string Text);
public record Link(string Text, string Url);

/// <summary>A party reference. A null PartyRef means "Independent / no party affiliation".</summary>
public record PartyRef(int Id, string Abbreviation, string Name)
{
    /// <summary>
    /// "No party was recorded for that date". This is NOT the same as independent (a null PartyRef): the directory
    /// never assumes a person's party, and never assumes today's party for an earlier year.
    /// </summary>
    public static readonly PartyRef NotRecorded = new(0, "Not recorded", "Party not recorded for this date");
    public bool IsRecorded => Id != 0;
}

public record PagedInfo(int Page, int PageSize, int TotalItems)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
}

public record SourceRef(string Title, string? Url, DateOnly? PublishedOn, DateOnly? LastChecked, string VerificationStatus);

// ----- People -----------------------------------------------------------------

public record PersonCardModel(int Id, string FullName, string? PhotoUrl, string PositionTitle, PartyRef? Party, string? Context = null);

public record PartyAffiliationDto(PartyRef? Party, DateOnly Start, DateOnly? End)
{
    public string Label => Party?.Abbreviation ?? "Independent";
}

/// <summary>One independent service record (appointment, term, office). A person has many.</summary>
public record ServiceRecord(
    string Kind,
    string Title,
    string? Detail,
    string? Administration,
    DateOnly Start,
    DateOnly? End,
    string AppointmentStatus,
    PartyRef? PartyAtTime,
    Link? RelatedPage,
    SourceRef? Source)
{
    public bool IsCurrent => End is null;
}

public record ElectionHistoryItem(int Year, string Contest, string? Constituency, PartyRef? PartyAtTime, ResultStatus Status, string? Outcome);

public record PersonProfileModel(
    int Id,
    string FullName,
    string? PhotoUrl,
    string Biography,
    IReadOnlyList<PartyAffiliationDto> Affiliations,
    IReadOnlyList<ServiceRecord> ServiceRecords,
    IReadOnlyList<ElectionHistoryItem> ElectionHistory,
    IReadOnlyList<SourceRef> Sources,
    IReadOnlyList<Link> AppearsOn);

// ----- Elections --------------------------------------------------------------

public record ElectionCycleSummary(
    int Id,
    int Year,
    string Name,
    ElectionPhase Phase,
    bool CandidatesPublished,
    ResultStatus PresidentialStatus,
    ResultStatus ParliamentaryStatus);

public record CandidateModel(
    int PersonId,
    string FullName,
    string? PhotoUrl,
    PartyRef? Party,
    string Status,
    string? RunningMate,
    string? Biography,
    long? Votes,
    decimal? Percentage,
    bool IsDeclaredWinner);

public record ResultTotals(long? ValidVotes, long? RejectedBallots, long? TotalVotesCast, long? RegisteredVoters);
public record ChangeLogItem(DateOnly On, string Summary);

public record ContestResult(
    ResultStatus Status,
    ResultTotals Totals,
    IReadOnlyList<SourceRef> Sources,
    DateOnly? VerifiedOn,
    DateOnly? PublishedOn,
    string? CorrectionNote,
    IReadOnlyList<ChangeLogItem> Changes);

public record PresidentialPageModel(
    IReadOnlyList<ElectionCycleSummary> Cycles,
    ElectionCycleSummary Selected,
    IReadOnlyList<CandidateModel> Candidates,
    ContestResult? Result);

public record ParliamentaryPageModel(
    IReadOnlyList<ElectionCycleSummary> Cycles,
    ElectionCycleSummary Selected,
    IReadOnlyList<Option> Regions,
    IReadOnlyList<Option> Constituencies,
    int? RegionId,
    int? ConstituencyId,
    string? RegionName,
    string? ConstituencyName,
    IReadOnlyList<CandidateModel> Candidates,
    ContestResult? Result);

// ----- Parties ----------------------------------------------------------------

public record PartyFilter(int? Year, int? AdministrationId, string? Position, int? RegionId, int Page = 1);

public record PartyPersonRow(
    PersonCardModel Person,
    string Position,
    string Administration,
    string? Region,
    string? Constituency,
    int? ElectionYear,
    DateOnly? Start,
    DateOnly? End,
    PartyRef? PartyAtTime);

public record PartyElectionRow(int Year, string PresidentialCandidate, ResultStatus Status);

public record PartyPageModel(
    PartyRef Party,
    string Description,
    string? Website,
    PartyFilter Filter,
    IReadOnlyList<PartyRef> AllParties,
    IReadOnlyList<Option> Years,
    IReadOnlyList<Option> Administrations,
    IReadOnlyList<Option> Positions,
    IReadOnlyList<Option> Regions,
    IReadOnlyList<PartyPersonRow> Rows,
    PagedInfo Paging,
    IReadOnlyList<PartyElectionRow> ElectionHistory);

// ----- Parliament -------------------------------------------------------------

public record ParliamentFilter(string? Term, int? RegionId, int? ConstituencyId, int? PartyId, string? Name, int Page = 1);

public record MpRow(PersonCardModel Person, string Term, string Region, string Constituency, PartyRef? PartyDuringTerm, string? OtherPositions);

public record ParliamentPageModel(
    ParliamentFilter Filter,
    IReadOnlyList<Option> Terms,
    IReadOnlyList<Option> Regions,
    IReadOnlyList<Option> Constituencies,
    IReadOnlyList<Option> Parties,
    IReadOnlyList<MpRow> Rows,
    PagedInfo Paging);

// ----- Ministries and offices -------------------------------------------------

public record OfficeholderRow(PersonCardModel Person, string Role, DateOnly Start, DateOnly? End);
public record MinistryNameEraDto(string Name, DateOnly Start, DateOnly? End, string? Note);
public record PublicOfficeModel(int Id, string Title, string Description, PersonCardModel? Holder, string? Administration);

public record MinistryPageModel(
    IReadOnlyList<Option> Ministries,
    IReadOnlyList<Option> Administrations,
    int MinistryId,
    int AdministrationId,
    string MinistryName,
    string Description,
    string? Website,
    OfficeholderRow? Minister,
    IReadOnlyList<OfficeholderRow> Deputies,
    IReadOnlyList<OfficeholderRow> Others,
    IReadOnlyList<MinistryNameEraDto> NameHistory,
    IReadOnlyList<OfficeholderRow> PreviousLeaders);

public record OfficesPageModel(IReadOnlyList<Option> Administrations, int AdministrationId, IReadOnlyList<PublicOfficeModel> Offices);

// ----- Presidency, archive ----------------------------------------------------

public record AdministrationSummary(
    int Id,
    string Name,
    DateOnly Start,
    DateOnly? End,
    string President,
    string VicePresident,
    int? ElectionYear,
    bool IsCurrent,
    bool IsDraft);

public record PresidencyRow(AdministrationSummary Administration, PersonCardModel? President, PersonCardModel? VicePresident);

public record TimelineEvent(DateOnly On, string Text, string Kind);
public record SeatRow(string PartyAbbreviation, int Seats);
public record CompareRow(string Label, string A, string? B);

public record ArchivePageModel(
    IReadOnlyList<AdministrationSummary> Administrations,
    AdministrationSummary? Selected,
    AdministrationSummary? Compare,
    IReadOnlyList<TimelineEvent> Timeline,
    IReadOnlyList<SeatRow> SeatsSelected,
    IReadOnlyList<SeatRow>? SeatsCompare,
    IReadOnlyList<CompareRow> CompareRows);

// ----- Home, search, static ---------------------------------------------------

public record HomeViewModel(
    IReadOnlyList<ElectionCycleSummary> Cycles,
    ElectionCycleSummary FeaturedCycle,
    AdministrationSummary? CurrentAdministration,
    IReadOnlyList<PersonCardModel> Government,
    IReadOnlyList<AdministrationSummary> Administrations);

public record SearchResultItem(string Label, string Type, string? Subtitle, string Url);
public record SearchPageModel(string? Query, IReadOnlyList<SearchResultItem> Items);

public record StaticPageModel(string Title, string Intro, IReadOnlyList<(string Heading, string Body)> Sections);
