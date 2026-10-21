using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Domain;
namespace Ghanapoliticaldirectory.Service.Business.Import;

    public class ImportModels
    {
    }


public sealed record ImportIssue(int Row, string Column, string Message);

/// <summary>One row of the sheet turned into a position request, plus a readable description for the preview.</summary>
public sealed record ImportPosition(int Row, PositionRequest Request, string Description);

/// <summary>All the rows that belong to one person. Imported together or not at all (when it has any issue).</summary>
public sealed class ImportPersonPlan
{
    public string Key { get; init; } = "";
    public string FullName { get; set; } = "";
    public List<int> Rows { get; } = new();
    public int? ExistingPersonId { get; set; }
    public string? ExistingPersonName { get; set; }
    public string? Biography { get; set; }
    public int? PartyId { get; set; }
    public string? PartyLabel { get; set; }
    public bool IndependentNoParty { get; set; }
    public DateOnly? PartySince { get; set; }
    public bool ConfirmedNotDuplicate { get; set; }
    public List<ImportPosition> Positions { get; } = new();
    public List<ImportIssue> Issues { get; } = new();
    public bool IsReady => Issues.Count == 0 && Positions.Count > 0;
}

public sealed class ImportPlan
{
    public string Sheet { get; init; } = "";
    public int DataRows { get; init; }
    public List<ImportIssue> FileIssues { get; } = new();
    public List<ImportPersonPlan> People { get; } = new();
    public bool HasFileProblem => FileIssues.Count > 0;
    public int ReadyCount => People.Count(p => p.IsReady);
    public int ProblemCount => People.Count(p => !p.IsReady);
}





public enum ImportStatus { Imported, PartlyImported, Failed, Skipped }

public sealed record ImportPersonResult(string FullName, ImportStatus Status, int? PersonId, int PositionsSaved, IReadOnlyList<string> Messages);

public sealed record ImportOutcome(IReadOnlyList<ImportPersonResult> People)
{
    public int Imported => People.Count(p => p.Status == ImportStatus.Imported);
    public int Partly => People.Count(p => p.Status == ImportStatus.PartlyImported);
    public int Failed => People.Count(p => p.Status == ImportStatus.Failed);
    public int Skipped => People.Count(p => p.Status == ImportStatus.Skipped);
}




public sealed record PartyLookup(int Id, string Abbreviation, string Name);
public sealed record AdministrationLookup(int Id, string Name, DateOnly Start, DateOnly? End);
public sealed record MinistryLookup(int Id, string Name, IReadOnlyList<string> AllNames);
public sealed record CycleLookup(int Id, int Year, string Name);
public sealed record SeatLookup(int Id, string Name, string Region);
public sealed record PersonLookup(int Id, string Name, bool IsArchived);





public sealed record ImportLookups(
    IReadOnlyList<PartyLookup> Parties,
    IReadOnlyList<AdministrationLookup> Administrations,
    IReadOnlyList<MinistryLookup> Ministries,
    IReadOnlyList<CycleLookup> Cycles,
    IReadOnlyList<SeatLookup> Seats,
    IReadOnlyList<PersonLookup> People);