namespace Ghanapoliticaldirectory.Models;

// Small models for reusable partial views, plus helpers so status wording lives in ONE place.

public record PortraitModel(string Name, string? PhotoUrl, string Size = "md");
public record PartyChipModel(PartyRef? Party, bool ShowName = false);
public record PageHeaderModel(string Title, string? Subtitle = null, IReadOnlyList<Link>? Crumbs = null);
public record EmptyStateModel(string Icon, string Title, string Message);
public record ResultsTableModel(string ContestLabel, IReadOnlyList<CandidateModel> Candidates, ContestResult Result);
/// <param name="Role">Optional data-role hook for the dependent Region → Constituency script (region | constituency).</param>
public record SelectFieldModel(string Name, string Label, IReadOnlyList<Option> Options, string? Selected, string Placeholder,
    bool Disabled = false, string? Role = null, string? Help = null, bool Required = false);
public record PositionPanelModel(string Key, string Label, PositionInput Input, AdminFormOptions Options, bool Open, string ConstituencyUrl);
public record SeatsModel(IReadOnlyList<SeatRow> Rows, int Max);
public record CycleSwitcherModel(IReadOnlyList<ElectionCycleSummary> Cycles, int SelectedYear, string Action, int? RegionId = null, int? ConstituencyId = null);

public static class UiHelpers
{
    public static string StatusLabel(ResultStatus s) => s switch
    {
        ResultStatus.Provisional => "Provisional",
        ResultStatus.Verified => "Verified",
        ResultStatus.OfficiallyDeclared => "Officially declared",
        ResultStatus.Corrected => "Corrected",
        ResultStatus.Published => "Published",
        _ => "Results not yet published"
    };

    public static string StatusClass(ResultStatus s) => s switch
    {
        ResultStatus.Provisional => "gpd-badge--provisional",
        ResultStatus.Verified => "gpd-badge--verified",
        ResultStatus.OfficiallyDeclared => "gpd-badge--declared",
        ResultStatus.Corrected => "gpd-badge--corrected",
        ResultStatus.Published => "gpd-badge--published",
        _ => "gpd-badge--none"
    };

    // Icons give every state a non-colour cue.
    public static string StatusIcon(ResultStatus s) => s switch
    {
        ResultStatus.Provisional => "bi-hourglass-split",
        ResultStatus.Verified => "bi-patch-check",
        ResultStatus.OfficiallyDeclared => "bi-check-circle-fill",
        ResultStatus.Corrected => "bi-pencil-square",
        ResultStatus.Published => "bi-globe2",
        _ => "bi-dash-circle"
    };

    public static string StatusHelp(ResultStatus s) => s switch
    {
        ResultStatus.Provisional => "Entered from an early source and not yet checked against the official publication.",
        ResultStatus.Verified => "Checked against the official publication by a reviewer. Awaiting official declaration where applicable.",
        ResultStatus.OfficiallyDeclared => "Officially declared by the Electoral Commission and verified against that source.",
        ResultStatus.Corrected => "A previously published figure was corrected. The change history is kept.",
        ResultStatus.Published => "Reviewed, approved and published.",
        _ => "No results have been published for this contest."
    };

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : parts.Length == 1 ? parts[0][..1] : $"{parts[0][0]}{parts[^1][0]}";
    }

    public static string Date(DateOnly d) => d.ToString("d MMM yyyy");
    public static string Range(DateOnly start, DateOnly? end) => $"{Date(start)} – {(end is null ? "present" : Date(end.Value))}";
}
