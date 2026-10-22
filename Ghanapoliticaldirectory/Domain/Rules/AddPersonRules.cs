using Ghanapoliticaldirectory.Domain.Rules;

namespace Ghanapoliticaldirectory.Domain.Rules;

public sealed record PositionRequest(
    PositionKind Kind,
    string? Title = null,
    int? ElectionCycleId = null,
    int? ConstituencyId = null,
    int? PartyAtElectionId = null,
    bool IndependentAtElection = false,
    int? AdministrationId = null,
    int? MinistryId = null,
    DateOnly? Start = null,
    DateOnly? End = null,
    AppointmentStatus? Status = null,
    string? SourceTitle = null,
    string? SourceUrl = null);

/// <summary>
/// What the "Add person" form submits. The tick-boxes are only a convenience: each ticked position becomes its
/// own related record (one appointment or parliamentary term), all pointing at the same single person.
/// </summary>
public sealed record AddPersonRequest(
    int? ExistingPersonId,
    string? FullName,
    string? Biography,
    string? AdditionalInfo,
    int? PartyId,
    bool IndependentNoParty,
    DateOnly? PartyStart,
    bool ConfirmedNotDuplicate,
    IReadOnlyList<PositionRequest> Positions);

public static class AddPersonValidator
{
    public static ValidationReport Validate(AddPersonRequest request)
    {
        var r = new ValidationReport();

        if (request.ExistingPersonId is null)
        {
            var name = request.FullName?.Trim() ?? "";
            if (name.Length == 0) r.Error("person.name_required", "Enter the person's full name.", "FullName");
            else if (name.Length < 2) r.Error("person.name_too_short", "The full name is too short.", "FullName");
            else if (name.Length > 200) r.Error("person.name_too_long", "The full name must be 200 characters or fewer.", "FullName");
        }

        if ((request.PartyId is not null || request.IndependentNoParty) && request.PartyStart is null)
            r.Error("affiliation.start_required",
                "Enter the date the party affiliation started (use the earliest date you can source).", "PartyStart");

        if (request.PartyId is not null && request.IndependentNoParty)
            r.Error("affiliation.conflict", "Choose a party or 'independent', not both.", "PartyId");

        var duplicateKind = request.Positions
            .GroupBy(p => p.Kind)
            .FirstOrDefault(g => g.Count() > 1 && g.Key != PositionKind.Other);
        if (duplicateKind is not null)
            r.Error("person.duplicate_position", $"{PositionDefaults.For(duplicateKind.Key).Title} is listed more than once. Add each position once; use the end date to close it.", "SelectedPositions");

        foreach (var p in request.Positions)
        {
            var label = PositionDefaults.For(p.Kind).Title;
            var rule = PositionDefaults.For(p.Kind);

            if (p.Start is null)
                r.Error("position.start_required", $"{label}: enter a start date.", $"{p.Kind}.Start");
            else if (p.End is not null && p.End < p.Start)
                r.Error("position.end_before_start", $"{label}: the end date cannot be before the start date.", $"{p.Kind}.End");

            if (p.Kind == PositionKind.MemberOfParliament)
            {
                if (p.ElectionCycleId is null)
                    r.Error("position.cycle_required", "Member of Parliament: select the election cycle or term.", $"{p.Kind}.ElectionCycleId");
                if (p.ConstituencyId is null)
                    r.Error("position.constituency_required", "Member of Parliament: select a region and constituency.", $"{p.Kind}.ConstituencyId");
                if (p.AdministrationId is null)
                    r.Error("position.administration_required", "Member of Parliament: select the administration they served under.", $"{p.Kind}.AdministrationId");
               
                if (p.PartyAtElectionId is not null && p.IndependentAtElection)
                    r.Error("position.party_conflict", "Member of Parliament: choose a party or 'independent', not both.", $"{p.Kind}.PartyAtElectionId");
            }
            else
            {
                if (rule.RequiresAdministration && p.AdministrationId is null)
                    r.Error("position.administration_required", $"{label}: select the administration.", $"{p.Kind}.AdministrationId");
                if (rule.RequiresMinistry && p.MinistryId is null)
                    r.Error("position.ministry_required", $"{label}: select the ministry.", $"{p.Kind}.MinistryId");
            }

            if (p.Kind == PositionKind.Other && string.IsNullOrWhiteSpace(p.Title))
                r.Error("position.title_required", "Other public office: enter the position title.", $"{p.Kind}.Title");

            if (!string.IsNullOrWhiteSpace(p.SourceUrl) && !IsHttpUrl(p.SourceUrl))
                r.Error("position.source_url_invalid", $"{label}: the source must be a web address starting with http:// or https://.", $"{p.Kind}.SourceUrl");
        }

        return r;
    }

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed record DetectedImage(string Extension, string ContentType);

/// <summary>Server-side checks for uploaded photographs and logos. File names and browser-reported types are never trusted.</summary>
public static class UploadRules
{
    public const long MaxImageBytes = 2 * 1024 * 1024;

    public static DetectedImage? DetectImage(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return new DetectedImage(".jpg", "image/jpeg");

        if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return new DetectedImage(".png", "image/png");

        if (header.Length >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
            return new DetectedImage(".webp", "image/webp");

        return null;
    }

    public static ValidationReport ValidateImage(long length, ReadOnlySpan<byte> header)
    {
        var r = new ValidationReport();
        if (length <= 0) r.Error("upload.empty", "The file is empty.", "Photo");
        else if (length > MaxImageBytes) r.Error("upload.too_large", "The image must be 2 MB or smaller.", "Photo");
        else if (DetectImage(header) is null) r.Error("upload.type", "Upload a JPG, PNG or WebP image.", "Photo");
        return r;
    }
}
