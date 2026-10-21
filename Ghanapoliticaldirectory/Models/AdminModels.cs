using System.ComponentModel.DataAnnotations;

namespace Ghanapoliticaldirectory.Models;

// ----- Dashboard / lists ------------------------------------------------------

public record SummaryCard(string Label, int Value, string IconClass, string? Hint, string? Url);
public record AwaitingItem(string Record, string Type, string EnteredBy, DateOnly Date, string Stage);
public record AuditEntry(DateTime At, string User, string Action, string Entity);

public record AdminDashboardModel(
    IReadOnlyList<SummaryCard> Cards,
    IReadOnlyList<AwaitingItem> Awaiting,
    IReadOnlyList<AuditEntry> Audit);

public record AdminPersonRow(int Id, string FullName, string Positions, string Party, string Status, DateOnly Updated);
public record AdminPeopleModel(string? Q, string? Status, IReadOnlyList<AdminPersonRow> Rows, PagedInfo Paging);

public record SimilarPerson(int Id, string FullName, string Summary);

public record AdminFormOptions(
    IReadOnlyList<Option> Parties,
    IReadOnlyList<Option> ElectionCycles,
    IReadOnlyList<Option> Administrations,
    IReadOnlyList<Option> Ministries,
    IReadOnlyList<Option> Regions,
    IReadOnlyList<Option> AppointmentStatuses);


public record AddPersonPageModel(AddPersonForm Form, AdminFormOptions Options);

// ----- Add Person form (one person, many independent position records) --------

/// <summary>
/// The checkboxes are only a UI convenience. On save the backend must create ONE People row
/// and one separate Appointment / ParliamentaryTerm row (with PersonId) per ticked position.
/// </summary>
public class PositionInput
{
    public string? ElectionCycleId { get; set; }
    public string? RegionId { get; set; }
    public string? ConstituencyId { get; set; }
    public string? PartyAtTimeId { get; set; }
    public string? AdministrationId { get; set; }
    public string? MinistryId { get; set; }
    public string? Title { get; set; }
    public DateOnly? Start { get; set; }
    public DateOnly? End { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
}

public class AddPersonForm : IValidatableObject
{
    public static readonly (string Key, string Label)[] PositionKinds =
    {
        ("President", "President"),
        ("VicePresident", "Vice President"),
        ("Mp", "Member of Parliament"),
        ("Minister", "Minister"),
        ("DeputyMinister", "Deputy Minister"),
        ("ChiefOfStaff", "Chief of Staff"),
        ("Speaker", "Speaker of Parliament"),
        ("Other", "Other public office")
    };

    /// <summary>Set when the administrator picked an existing person: no new People row is created.</summary>
    public int? ExistingPersonId { get; set; }

    [Required(ErrorMessage = "Enter the person's full name."), StringLength(150)]
    [Display(Name = "Full name")]
    public string? FullName { get; set; }

    public IFormFile? Photo { get; set; }

    [StringLength(8000)]
    public string? Biography { get; set; }

    [Display(Name = "Current party")]
    public string? PartyId { get; set; }
    public DateOnly? PartyStart { get; set; }

    [StringLength(2000)]
    public string? AdditionalInfo { get; set; }

    public List<string> SelectedPositions { get; set; } = new();

    public PositionInput President { get; set; } = new();
    public PositionInput VicePresident { get; set; } = new();
    public PositionInput Mp { get; set; } = new();
    public PositionInput Minister { get; set; } = new();
    public PositionInput DeputyMinister { get; set; } = new();
    public PositionInput ChiefOfStaff { get; set; } = new();
    public PositionInput Speaker { get; set; } = new();
    public PositionInput Other { get; set; } = new();

    public PositionInput For(string key) => key switch
    {
        "President" => President,
        "VicePresident" => VicePresident,
        "Mp" => Mp,
        "Minister" => Minister,
        "DeputyMinister" => DeputyMinister,
        "ChiefOfStaff" => ChiefOfStaff,
        "Speaker" => Speaker,
        _ => Other
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (key, label) in PositionKinds.Where(k => SelectedPositions.Contains(k.Key)))
        {
            var p = For(key);
            var prefix = key;

            if (p.Start is null)
                yield return new ValidationResult($"{label}: enter a start date.", new[] { $"{prefix}.{nameof(PositionInput.Start)}" });

            if (p.Start is not null && p.End is not null && p.End < p.Start)
                yield return new ValidationResult($"{label}: the end date cannot be before the start date.", new[] { $"{prefix}.{nameof(PositionInput.End)}" });

            if (key == "Mp")
            {
                if (string.IsNullOrEmpty(p.ElectionCycleId))
                    yield return new ValidationResult("Member of Parliament: select the election cycle or term.", new[] { "Mp.ElectionCycleId" });
                if (string.IsNullOrEmpty(p.ConstituencyId))
                    yield return new ValidationResult("Member of Parliament: select a region and constituency.", new[] { "Mp.ConstituencyId" });
            }

            if (key is "Minister" or "DeputyMinister")
            {
                if (string.IsNullOrEmpty(p.AdministrationId))
                    yield return new ValidationResult($"{label}: select the administration.", new[] { $"{prefix}.AdministrationId" });
                if (string.IsNullOrEmpty(p.MinistryId))
                    yield return new ValidationResult($"{label}: select the ministry.", new[] { $"{prefix}.MinistryId" });
            }

            if (key == "Other" && string.IsNullOrWhiteSpace(p.Title))
                yield return new ValidationResult("Other public office: enter the position title.", new[] { "Other.Title" });
        }
    }
}

// ----- Election cycles and constituencies --------------------------------------

public class AddCycleForm
{
    [Required(ErrorMessage = "Enter the election year."), Range(1950, 2100, ErrorMessage = "Enter a four-digit year.")]
    public int? Year { get; set; }
    [StringLength(150)] public string? Name { get; set; }
    public DateOnly? ElectionDate { get; set; }
    public bool IsDateConfirmed { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
}

public class AddConstituencyForm
{
    [Required(ErrorMessage = "Select a region.")] public int? RegionId { get; set; }
    [Required(ErrorMessage = "Enter the constituency name."), StringLength(150)] public string? Name { get; set; }
}

public record ElectionsPageModel(IReadOnlyList<Option> Cycles, AddCycleForm Form);
public record RegionsPageModel(IReadOnlyList<Option> Regions, int? SelectedRegionId, IReadOnlyList<Option> Constituencies, AddConstituencyForm Form);

// ----- Administrations and ministries ------------------------------------------

public class AddAdministrationForm
{
    [Required(ErrorMessage = "Enter the administration's name."), StringLength(150)] public string? Name { get; set; }
    [Required(ErrorMessage = "Enter the start date.")] public DateOnly? Start { get; set; }
    public string? ElectionCycleId { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
}

public class AddMinistryForm
{
    [Required(ErrorMessage = "Enter the ministry's name."), StringLength(200)] public string? Name { get; set; }
    [Required(ErrorMessage = "Enter the date this name began.")] public DateOnly? NameSince { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
}

public record AdministrationsPageModel(
    IReadOnlyList<Ghanapoliticaldirectory.Business.AdministrationRow> Rows, IReadOnlyList<Option> Cycles, AddAdministrationForm Form);
public record MinistriesAdminModel(
    IReadOnlyList<Ghanapoliticaldirectory.Business.MinistryRow> Rows, AddMinistryForm Form);
