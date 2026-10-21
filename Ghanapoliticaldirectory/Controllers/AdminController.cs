//using Ghanapoliticaldirectory.Domain;
//using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Rules;
//using Ghanapoliticaldirectory.Service;
//using Ghanapoliticaldirectory.Service.Business;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using static System.Collections.Specialized.BitVector32;

using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Ghanapoliticaldirectory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ghanapoliticaldirectory.Controllers;

[Route("admin")]
[Authorize(Policy = "perm:" + Permissions.AdminAccess)]
public class AdminController : Controller
{
    private readonly IDirectoryReader _data;
    private readonly PersonService _people;
    private readonly PhotoService _photos;
    private readonly WorkflowService _workflow;
    private readonly GovernmentService _government;
    private readonly ReferenceDataService _reference;

    public AdminController(IDirectoryReader data, PersonService people, PhotoService photos,
        WorkflowService workflow, GovernmentService government, ReferenceDataService reference)
    { _data = data; _people = people; _photos = photos; _workflow = workflow; _government = government; _reference = reference; }

    [HttpGet("")]
    public IActionResult Dashboard() => View(_data.GetAdminDashboard());

    // ----- People ---------------------------------------------------------------

    [HttpGet("people")]
    public IActionResult People(string? q, string? status, int page = 1) =>
        View(_data.GetAdminPeople(q, status, page));

    [HttpGet("people/similar")]
    public IActionResult Similar(string? name) => Json(_data.FindSimilarPeople(name ?? ""));

    [HttpGet("people/add")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public IActionResult AddPerson(int? existingPersonId)
    {
        var form = new AddPersonForm();
        if (existingPersonId is not null && _data.GetPerson(existingPersonId.Value) is { } existing)
        {
            form.ExistingPersonId = existing.Id;
            form.FullName = existing.FullName;
        }
        return View(new AddPersonPageModel(form, _data.GetFormOptions()));
    }

    [HttpPost("people/add")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> AddPerson(AddPersonForm form, bool confirmedNotDuplicate, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(new AddPersonPageModel(form, _data.GetFormOptions()));

        var result = await _people.AddAsync(ToRequest(form, confirmedNotDuplicate), ct);

        if (result.Value?.Duplicates.Count > 0 && !result.Ok)
        {
            ModelState.AddModelError("", "A similar person already exists: " +
                string.Join(", ", result.Value.Duplicates.Select(d => d.FullName)) +
                ". Add a position to that profile, or tick \"this is a different person\" and save again.");
        }
        foreach (var e in result.Report.Errors)
            ModelState.AddModelError(e.Field ?? "", e.Message);

        if (!result.Ok)
            return View(new AddPersonPageModel(form, _data.GetFormOptions()));

        var outcome = result.Value!;
        var photoNote = "";
        if (form.Photo is { Length: > 0 } photo)
        {
            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, ct);
            var saved = await _photos.SetPhotoAsync(outcome.PersonId, ms.ToArray(), form.FullName, ct);
            if (!saved.Ok) photoNote = " The photo was not saved: " + saved.FirstError;
        }

        TempData["Success"] = (outcome.CreatedNewPerson
            ? $"Saved as draft: 1 new person and {outcome.CreatedRecords.Count} position record(s)."
            : $"Saved as draft: {outcome.CreatedRecords.Count} new position record(s) added to the existing profile.")
            + " Nothing is public until it is verified, approved and published." + photoNote;
        return RedirectToAction(nameof(People));
    }
     
    private static AddPersonRequest ToRequest(AddPersonForm f, bool confirmed)
    {
        // Blank party = not recorded (unknown). "independent" = recorded as independent. Never conflated.
        var independent = f.PartyId == EfDirectoryReader.IndependentOption;
        int? partyId = int.TryParse(f.PartyId, out var pid) ? pid : null;

        var positions = new List<PositionRequest>();
        foreach (var (key, _) in AddPersonForm.PositionKinds.Where(k => f.SelectedPositions.Contains(k.Key)))
        {
            var p = f.For(key);
            var atIndependent = p.PartyAtTimeId == EfDirectoryReader.IndependentOption;
            var src = p.Source?.Trim();
            var isUrl = src is not null && Uri.TryCreate(src, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
            positions.Add(new PositionRequest(
                Kind: key switch
                {
                    "President" => PositionKind.President,
                    "VicePresident" => PositionKind.VicePresident,
                    "Mp" => PositionKind.MemberOfParliament,
                    "Minister" => PositionKind.Minister,
                    "DeputyMinister" => PositionKind.DeputyMinister,
                    "ChiefOfStaff" => PositionKind.ChiefOfStaff,
                    "Speaker" => PositionKind.Speaker,
                    _ => PositionKind.Other
                },
                Title: p.Title,
                ElectionCycleId: Int(p.ElectionCycleId),
                ConstituencyId: Int(p.ConstituencyId),
                PartyAtElectionId: atIndependent ? null : Int(p.PartyAtTimeId),
                IndependentAtElection: atIndependent,
                AdministrationId: Int(p.AdministrationId),
                MinistryId: Int(p.MinistryId),
                Start: p.Start,
                End: p.End,
                Status: Enum.TryParse<AppointmentStatus>(p.Status, out var st) ? st : null,
                SourceTitle: isUrl ? null : src,
                SourceUrl: isUrl ? src : null));
        }

        return new AddPersonRequest(f.ExistingPersonId, f.FullName, f.Biography, f.AdditionalInfo,
            partyId, independent, f.PartyStart, confirmed, positions);
    }

    private static int? Int(string? s) => int.TryParse(s, out var i) ? i : null;

    [HttpPost("people/{id:int}/archive")]
    [Authorize(Policy = "perm:" + Permissions.RecordsArchive)]
    public async Task<IActionResult> ArchivePerson(int id, string? reason, CancellationToken ct)
    {
        var r = await _government.ArchiveAsync<Person>(id, string.IsNullOrWhiteSpace(reason) ? "Archived from the people list" : reason!, ct);
        TempData["Success"] = r.Ok ? "Archived. The profile and its history are kept; it is only hidden from public pages." : r.FirstError;
        return RedirectToAction(nameof(People));
    }

    // ----- Review queue (verify / approve / publish are separate steps) ----------------

    [HttpGet("review")]
    public async Task<IActionResult> Review(CancellationToken ct) => View(await _workflow.GetQueueAsync(null, 100, ct));

    [HttpPost("review/advance")]
    public async Task<IActionResult> Advance(WorkflowEntity type, int id, WorkflowAction action, string? notes, CancellationToken ct)
    {
        var r = await _workflow.AdvanceAsync(type, id, action, notes, includeRelated: false, ct);
        TempData["Success"] = r.Ok
            ? $"Done. The record is now {r.Value!.NewStage}."
            : r.FirstError ?? "That step is not allowed.";
        return RedirectToAction(nameof(Review));
    }

    // ----- Election cycles and constituencies (must exist before MPs or candidates can be attached) ---

    [HttpGet("elections")]
    public IActionResult Elections() => View(new ElectionsPageModel(_data.GetElectionCycleOptions(), new AddCycleForm()));

    [HttpPost("elections")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Elections(AddCycleForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var r = await _reference.AddCycleAsync(new AddCycleRequest(form.Year ?? 0, form.Name, form.ElectionDate, form.IsDateConfirmed, form.Notes), ct);
            if (r.Ok)
            {
                TempData["Success"] = "Election cycle saved as a draft. It appears on public pages only after it is verified, approved and published (see the review queue).";
                return RedirectToAction(nameof(Elections));
            }
            foreach (var e in r.Report.Errors) ModelState.AddModelError("", e.Message);
        }
        return View(new ElectionsPageModel(_data.GetElectionCycleOptions(), form));
    }

    [HttpGet("regions")]
    public IActionResult Regions(int? regionId) =>
        View(new RegionsPageModel(_data.GetFormOptions().Regions, regionId,
            regionId is null ? Array.Empty<Option>() : _data.GetConstituencies(regionId.Value), new AddConstituencyForm { RegionId = regionId }));

    [HttpPost("regions")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Regions(AddConstituencyForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var r = await _reference.AddConstituencyAsync(form.RegionId ?? 0, form.Name, ct);
            if (r.Ok)
            {
                TempData["Success"] = $"Constituency \"{form.Name!.Trim()}\" added.";
                return RedirectToAction(nameof(Regions), new { regionId = form.RegionId });
            }
            foreach (var e in r.Report.Errors) ModelState.AddModelError("", e.Message);
        }
        return View(new RegionsPageModel(_data.GetFormOptions().Regions, form.RegionId,
            form.RegionId is null ? Array.Empty<Option>() : _data.GetConstituencies(form.RegionId.Value), form));
    }

    // ----- Administrations ------------------------------------------------------------

    [HttpGet("administrations")]
    public async Task<IActionResult> Administrations(CancellationToken ct) => View(await AdminsModel(new AddAdministrationForm(), ct));

    private async Task<AdministrationsPageModel> AdminsModel(AddAdministrationForm f, CancellationToken ct) =>
        new(await _reference.ListAdministrationsAsync(ct), _data.GetElectionCycleOptions(), f);

    [HttpPost("administrations")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Administrations(AddAdministrationForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var r = await _government.CreateAdministrationAsync(form.Name!, form.Start!.Value, Int(form.ElectionCycleId), form.Description, ct);
            if (r.Ok) { TempData["Success"] = "Administration saved as unconfirmed. Confirm it, then publish it from the review queue, before it appears as fact."; return RedirectToAction(nameof(Administrations)); }
            foreach (var e in r.Report.Errors) ModelState.AddModelError("", e.Message);
        }
        return View(await AdminsModel(form, ct));
    }

    [HttpPost("administrations/{id:int}/confirm")]
    [Authorize(Policy = "perm:" + Permissions.RecordsApprove)]
    public async Task<IActionResult> ConfirmAdministration(int id, CancellationToken ct)
    {
        var r = await _government.ConfirmAdministrationAsync(id, ct);
        TempData["Success"] = r.Ok ? "Administration confirmed." : r.FirstError;
        return RedirectToAction(nameof(Administrations));
    }

    [HttpPost("administrations/{id:int}/close")]
    [Authorize(Policy = "perm:" + Permissions.RecordsCorrect)]
    public async Task<IActionResult> CloseAdministration(int id, DateOnly? endDate, CancellationToken ct)
    {
        if (endDate is null) { TempData["Success"] = "Enter the end date.";
            return RedirectToAction(nameof(Administrations)); }
        var r = await _government.CloseAdministrationAsync(id, endDate.Value, ct);
        TempData["Success"] = r.Ok ? $"Administration closed. {r.Value} open appointment(s) belonging to it were ended on that date." : r.FirstError;
        return RedirectToAction(nameof(Administrations));
    }

    // ----- Ministries (names are kept with their dates) --------------------------------

    [HttpGet("ministries")]
    public async Task<IActionResult> Ministries(CancellationToken ct) => View(new MinistriesAdminModel(await _reference.ListMinistriesAsync(ct), new AddMinistryForm()));

    [HttpPost("ministries")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Ministries(AddMinistryForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var r = await _reference.AddMinistryAsync(form.Name, form.NameSince, form.Description, ct);
            if (r.Ok) { TempData["Success"] = "Ministry added."; return RedirectToAction(nameof(Ministries)); }
            foreach (var e in r.Report.Errors) ModelState.AddModelError("", e.Message);
        }
        return View(new MinistriesAdminModel(await _reference.ListMinistriesAsync(ct), form));
    }

    [HttpPost("ministries/{id:int}/rename")]
    [Authorize(Policy = "perm:" + Permissions.RecordsCorrect)]
    public async Task<IActionResult> RenameMinistry(int id, string? newName, DateOnly? effectiveFrom, string? note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newName) || effectiveFrom is null) { TempData["Success"] = "Enter the new name and the date it takes effect."; return RedirectToAction(nameof(Ministries)); }
        var r = await _government.RenameMinistryAsync(id, newName, effectiveFrom.Value, note, ct);
        TempData["Success"] = r.Ok ? "Renamed. The old name is kept with the dates it applied." : r.FirstError;
        return RedirectToAction(nameof(Ministries));
    }

    // ----- Audit log ---------------------------------------------------------------------

    [HttpGet("audit")]
    [Authorize(Policy = "perm:" + Permissions.AuditView)]
    public IActionResult Audit() => View("AuditLogs", _data.GetAdminDashboard().Audit);
}