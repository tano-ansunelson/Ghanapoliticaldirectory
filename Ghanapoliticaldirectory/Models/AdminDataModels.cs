using Ghanapoliticaldirectory.Business;
using System.ComponentModel.DataAnnotations;

namespace Ghanapoliticaldirectory.Models;

// ----- Parties ---------------------------------------------------------------------

public class AddPartyForm
{
    [Required(ErrorMessage = "Enter the party's name."), StringLength(200)] public string? Name { get; set; }
    [Required(ErrorMessage = "Enter the abbreviation."), StringLength(20)] public string? Abbreviation { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(300)] public string? WebsiteUrl { get; set; }
    public DateOnly? FoundedOn { get; set; }
}
public record PartiesPageModel(IReadOnlyList<PartyRow> Rows, AddPartyForm Form);

// ----- Candidates ------------------------------------------------------------------

//public class AddCandidateForm
//{
//    [Required(ErrorMessage = "Select the election cycle.")] public string? ElectionCycleId { get; set; }
//    [Required(ErrorMessage = "Select the contest type.")] public string? ContestType { get; set; } = "Parliamentary";
//    public string? ConstituencyId { get; set; }
//    public int Round { get; set; } = 1;
//    [Required(ErrorMessage = "Select the person.")] public string? PersonId { get; set; }
//    /// <summary>Blank = not chosen (rejected), "independent" = stood as an independent, otherwise a party id.</summary>
//    public string? PartyId { get; set; }
//    public string? RunningMatePersonId { get; set; }
//    public int? BallotNumber { get; set; }
//}

public class CandidateEntryRow
{
    /// <summary>An existing person's id. Leave empty and fill NewName for someone not in the system yet.</summary>
    public string? PersonId { get; set; }
    public string? NewName { get; set; }
    public bool ConfirmedNotDuplicate { get; set; }
    /// <summary>Blank = not chosen (rejected), "independent" = stood as an independent, otherwise a party id.</summary>
    public string? PartyId { get; set; }
    public string? RunningMatePersonId { get; set; }
    public string? RunningMateNewName { get; set; }
    public int? BallotNumber { get; set; }
    public bool IsBlank => string.IsNullOrWhiteSpace(PersonId) && string.IsNullOrWhiteSpace(NewName) && string.IsNullOrWhiteSpace(PartyId)
        && string.IsNullOrWhiteSpace(RunningMatePersonId) && string.IsNullOrWhiteSpace(RunningMateNewName) && BallotNumber is null;
}

public class AddCandidatesForm
{
    public string? ElectionCycleId { get; set; }
    public string? ContestType { get; set; } = "Parliamentary";
    public string? ConstituencyId { get; set; }
    public int Round { get; set; } = 1;
    public List<CandidateEntryRow> Rows { get; set; } = new();
}


public record CandidatesPageModel(
    AddCandidatesForm Form, IReadOnlyList<Option> Cycles, IReadOnlyList<Option> Parties,
    IReadOnlyList<(int Id, string Name)> People, IReadOnlyList<ConstituencyOption> Constituencies,
    IReadOnlyList<ContestRow> Contests, Ghanapoliticaldirectory.Business.ContestInfo? Current);

// ----- Results ---------------------------------------------------------------------

public class ResultFormLine
{
    public int CandidateId { get; set; }
    public string? Name { get; set; }
    public string? Party { get; set; }
    public long? Votes { get; set; }
}

public class ResultForm
{
    public int ContestId { get; set; }
    public string? ContestLabel { get; set; }
    public int? ResultId { get; set; }
    public string Basis { get; set; } = "Provisional";
    public long? ValidVotes { get; set; }
    public long? RejectedBallots { get; set; }
    public long? TotalVotesCast { get; set; }
    public long? RegisteredVoters { get; set; }
    public DateOnly? DeclaredOn { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? VerificationNotes { get; set; }
    /// <summary>The candidate the Electoral Commission declared the winner. Leave empty until it has.</summary>
    public int? WinnerCandidateId { get; set; }
    public List<ResultFormLine> Lines { get; set; } = new();
    public string? StageName { get; set; }
    public bool HasBeenCorrected { get; set; }
    /// <summary>save | submit | verify | approve | publish | return | unpublish | correct</summary>
    public string? Command { get; set; }
    public string? Notes { get; set; }
    public string? CorrectionReason { get; set; }
}

public record ResultsListModel(IReadOnlyList<Option> Cycles, int? CycleId, IReadOnlyList<ContestRow> Contests);

// ----- Person page -----------------------------------------------------------------

public class ChangePartyForm
{
    public string? PartyId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public string? Source { get; set; }
}

public record PersonAdminPageModel(PersonAdminView Person, IReadOnlyList<Option> Parties);

// ----- Users -----------------------------------------------------------------------

public class AddUserForm
{
    [Required(ErrorMessage = "Enter the email address."), EmailAddress] public string? Email { get; set; }
    [StringLength(150)] public string? DisplayName { get; set; }
    [Required(ErrorMessage = "Enter a password."), DataType(DataType.Password)] public string? Password { get; set; }
    [Required(ErrorMessage = "Select a role.")] public string? Role { get; set; }
}

public record UserRow(string Id, string Email, string? DisplayName, bool IsActive, string Roles);
public record UsersPageModel(IReadOnlyList<UserRow> Rows, AddUserForm Form, IReadOnlyList<string> Roles);

// ----- Lists -----------------------------------------------------------------------

public record AppointmentsListModel(string? Q, IReadOnlyList<ListedAppointment> Rows);
public record TermsListModel(string? Q, IReadOnlyList<ListedTerm> Rows);



/// <summary>The Import screen: the reference names, then either a check report or the result of an import.</summary>
public sealed record ImportPageModel(
    Ghanapoliticaldirectory.Service.Business.ImportReference Reference,
    Ghanapoliticaldirectory.Service.Business.Import.ImportPlan? Plan = null,
    Ghanapoliticaldirectory.Service.Business.Import.ImportOutcome? Outcome = null,
    string? FileName = null);



public class EditPersonForm
{
    public int Id { get; set; }

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Enter the person's full name."), System.ComponentModel.DataAnnotations.StringLength(200)]
    public string? FullName { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(8000)]
    public string? Biography { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(2000)]
    public string? AdditionalInfo { get; set; }

    /// <summary>Required once the profile has been reviewed; kept in the history with the previous text.</summary>
    [System.ComponentModel.DataAnnotations.StringLength(1000)]
    public string? Reason { get; set; }

    public bool ConfirmedNotDuplicate { get; set; }

    public Microsoft.AspNetCore.Http.IFormFile? Photo { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(300)]
    public string? PhotoAlt { get; set; }
}

public sealed record EditPersonPageModel(Ghanapoliticaldirectory.Business.PersonEditView Person, EditPersonForm Form);
