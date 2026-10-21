using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Models;
using Ghanapoliticaldirectory.Service.Business;
using Ghanapoliticaldirectory.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;


namespace Ghanapoliticaldirectory.Controllers;

[Route("admin")]
[Authorize(Policy = "perm:" + Permissions.AdminAccess)]
public class AdminDataController : Controller
{
    private readonly IDirectoryReader _data;
    private readonly AdminQueries _queries;
    private readonly ReferenceDataService _reference;
    private readonly CandidateService _candidates;
    private readonly ResultService _results;
    private readonly GovernmentService _government;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ImportService _import;
    private readonly PersonService _people;
    private readonly PhotoService _photos;
    public IActionResult Index()
    {
        return View();
    }

    public AdminDataController(IDirectoryReader data, AdminQueries queries, ReferenceDataService reference,
        CandidateService candidates, ResultService results, GovernmentService government, UserManager<ApplicationUser> users, ImportService import,
        PersonService people, PhotoService photos
        )
    { _photos = photos; _people = people; _import = import; _data = data; _queries = queries; _reference = reference; _candidates = candidates; _results = results; _government = government; _users = users; }

    private static int? Int(string? s) => int.TryParse(s, out var i) ? i : null;

    private void AddErrors(ServiceResult r)
    {
        foreach (var e in r.Report.Errors) ModelState.AddModelError("", e.Message);
    }

    // ----- Parties -------------------------------------------------------------------

    [HttpGet("parties")]
    public async Task<IActionResult> Parties(CancellationToken ct) => View(new PartiesPageModel(await _queries.PartiesAsync(ct), new AddPartyForm()));

    [HttpPost("parties")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Parties(AddPartyForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var r = await _reference.AddPartyAsync(form.Name, form.Abbreviation, form.Description, form.WebsiteUrl, form.FoundedOn, ct);
            if (r.Ok) { TempData["Success"] = "Party added."; return RedirectToAction(nameof(Parties)); }
            AddErrors(r);
        }
        return View(new PartiesPageModel(await _queries.PartiesAsync(ct), form));
    }

    // ----- Candidates ----------------------------------------------------------------
    private static ContestType ContestTypeOf(string? t) => t == "Presidential" ? ContestType.Presidential : ContestType.Parliamentary;
    private async Task<CandidatesPageModel> CandidatesModel(AddCandidatesForm f, CancellationToken ct)
    {
        ContestInfo? current = null;
        var type = ContestTypeOf(f.ContestType);
        if (Int(f.ElectionCycleId) is int cycle && (type == ContestType.Presidential || Int(f.ConstituencyId) is not null))
            current = await _queries.ContestInfoAsync(cycle, type, type == ContestType.Presidential ? null : Int(f.ConstituencyId), f.Round < 1 ? 1 : f.Round, ct);

        // always leave room for the winner and a few opponents
        while (f.Rows.Count < 4) f.Rows.Add(new CandidateEntryRow());

        return new CandidatesPageModel(f, _data.GetElectionCycleOptions(), _data.GetFormOptions().Parties,
            await _queries.PeopleOptionsAsync(ct), await _queries.ConstituenciesAsync(ct), await _queries.ContestsAsync(null, ct), current);
    }
    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(string? electionCycleId, string? contestType, string? constituencyId, int round = 1, int? personId = null, CancellationToken ct = default)
    {
        var form = new AddCandidatesForm { ElectionCycleId = electionCycleId, ContestType = contestType ?? "Parliamentary", ConstituencyId = constituencyId, Round = round < 1 ? 1 : round };
        if (personId is not null) form.Rows.Add(new CandidateEntryRow { PersonId = personId.ToString() });
        return View(await CandidatesModel(form, ct));
    }
    [HttpPost("candidates")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> Candidates(AddCandidatesForm form, CancellationToken ct)
    {
        form.Rows = (form.Rows ?? new()).Where(r => !r.IsBlank).ToList();
        var type = ContestTypeOf(form.ContestType);

        if (Int(form.ElectionCycleId) is null) ModelState.AddModelError("", "Select the election cycle.");
        if (type == ContestType.Parliamentary && Int(form.ConstituencyId) is null) ModelState.AddModelError("", "Select the constituency.");

        if (ModelState.IsValid)
        {
            var rows = form.Rows.Select(r =>
            {
                var independent = r.PartyId == EfDirectoryReader.IndependentOption;
                return new NewCandidate(Int(r.PersonId), r.NewName, r.ConfirmedNotDuplicate,
                    independent ? null : Int(r.PartyId), independent,
                    type == ContestType.Presidential ? Int(r.RunningMatePersonId) : null,
                    type == ContestType.Presidential ? r.RunningMateNewName : null, r.BallotNumber);
            }).ToList();

            var saved = await _candidates.AddManyAsync(new AddCandidatesRequest(
                Int(form.ElectionCycleId)!.Value, type, type == ContestType.Parliamentary ? Int(form.ConstituencyId) : null,
                form.Round < 1 ? 1 : form.Round, rows), ct);
            if (saved.Ok)
            {
                var o = saved.Value!;
                TempData["Success"] = $"Added {o.CandidatesAdded} candidate(s)" + (o.PeopleCreated > 0 ? $" and {o.PeopleCreated} new profile(s) with just a name" : "") +
                    ". They are drafts: send them through the review queue. You can now enter the votes under Election results.";
                return RedirectToAction(nameof(Candidates), new { electionCycleId = form.ElectionCycleId, contestType = form.ContestType, constituencyId = form.ConstituencyId, round = form.Round });
            }
            AddErrors(saved);
        }
        return View(await CandidatesModel(form, ct));
    }
    // ----- Election results ----------------------------------------------------------

    [HttpGet("results")]
    public async Task<IActionResult> Results(int? cycleId, CancellationToken ct) =>
        View(new ResultsListModel(_data.GetElectionCycleOptions(), cycleId, await _queries.ContestsAsync(cycleId, ct)));

    private async Task<ResultForm?> LoadResultForm(int contestId, CancellationToken ct)
    {
        var r = await _results.LoadForEditAsync(contestId, ct);
        if (!r.Ok) return null;
        var e = r.Value!;
        var contest = (await _queries.ContestsAsync(null, ct)).FirstOrDefault(c => c.Id == contestId);
        return new ResultForm
        {
            ContestId = contestId,
            ContestLabel = contest is null ? $"Contest #{contestId}" : $"{contest.Cycle} · {contest.Label}",
            ResultId = e.ResultId,
            Basis = e.Basis.ToString(),
            ValidVotes = e.ValidVotes,
            RejectedBallots = e.RejectedBallots,
            TotalVotesCast = e.TotalVotesCast,
            RegisteredVoters = e.RegisteredVoters,
            DeclaredOn = e.DeclaredOn,
            SourceTitle = e.SourceTitle,
            SourceUrl = e.SourceUrl,
            VerificationNotes = e.VerificationNotes,
            WinnerCandidateId = e.Lines.FirstOrDefault(l => l.IsDeclaredWinner)?.CandidateId,
            StageName = e.Stage?.ToString(),
            HasBeenCorrected = e.HasBeenCorrected,
            Lines = e.Lines.Select(l => new ResultFormLine { CandidateId = l.CandidateId, Name = l.Name, Party = l.PartyLabel, Votes = l.Votes }).ToList()
        };
    }

    [HttpGet("results/{contestId:int}")]
    public async Task<IActionResult> EditResult(int contestId, CancellationToken ct)
    {
        var form = await LoadResultForm(contestId, ct);
        return form is null ? NotFound() : View(form);
    }


    [HttpPost("results/{contestId:int}")]
    public async Task<IActionResult> EditResult(int contestId, ResultForm form, CancellationToken ct)
    {
        var fresh = await LoadResultForm(contestId, ct);
        if (fresh is null) return NotFound();
        // Names, labels and the stage always come from the database, never from the posted form.
        form.ContestId = contestId; form.ContestLabel = fresh.ContestLabel; form.StageName = fresh.StageName;
        form.ResultId = fresh.ResultId; form.HasBeenCorrected = fresh.HasBeenCorrected;
        var names = fresh.Lines.ToDictionary(l => l.CandidateId);
        form.Lines = form.Lines.Where(l => names.ContainsKey(l.CandidateId))
            .Select(l => new ResultFormLine { CandidateId = l.CandidateId, Name = names[l.CandidateId].Name, Party = names[l.CandidateId].Party, Votes = l.Votes }).ToList();

        var edit = new ResultEdit
        {
            ContestId = contestId,
            ResultId = fresh.ResultId,
            Basis = Enum.TryParse<ResultBasis>(form.Basis, out var b) ? b : ResultBasis.Provisional,
            ValidVotes = form.ValidVotes,
            RejectedBallots = form.RejectedBallots,
            TotalVotesCast = form.TotalVotesCast,
            RegisteredVoters = form.RegisteredVoters,
            DeclaredOn = form.DeclaredOn,
            SourceTitle = form.SourceTitle,
            SourceUrl = form.SourceUrl,
            VerificationNotes = form.VerificationNotes,
            Lines = form.Lines.Select(l => new ResultLineEdit
            { CandidateId = l.CandidateId, Name = l.Name ?? "", PartyLabel = l.Party, Votes = l.Votes, IsDeclaredWinner = l.CandidateId == form.WinnerCandidateId }).ToList()
        };

        var cmd = form.Command ?? "save";
        ServiceResult? failure = null;
        string? message = null;

        if (cmd == "correct")
        {
            var r = await _results.CorrectAsync(edit, form.CorrectionReason ?? "", ct);
            failure = r.Ok ? null : r; message = "Correction saved. The old figures are kept in the history and the public label now reads Corrected.";
        }
        else
        {
            var locked = fresh.StageName is not (null or "Draft" or "AwaitingVerification");
            int? resultId = fresh.ResultId;
            if (!locked)
            {
                var saved = await _results.SaveDraftAsync(edit, ct);
                if (!saved.Ok) failure = saved; else { resultId = saved.Value; message = "Draft saved. Nothing is public yet."; }
            }
            WorkflowAction? action = cmd switch
            {
                "submit" => WorkflowAction.Submit,
                "verify" => WorkflowAction.Verify,
                "approve" => WorkflowAction.Approve,
                "publish" => WorkflowAction.Publish,
                "return" => WorkflowAction.ReturnToDraft,
                "unpublish" => WorkflowAction.Unpublish,
                _ => null
            };
            if (failure is null && action is not null)
            {
                if (resultId is null) failure = Result.Fail(ServiceFailure.Invalid, "result.save_first", "Save the figures first.");
                else
                {
                    var adv = await _results.AdvanceAsync(resultId.Value, action.Value, form.Notes ?? form.VerificationNotes, ct);
                    if (adv.Ok) message = $"Done. The result is now {adv.Value}."; else failure = adv;
                }
            }
        }

        if (failure is not null)
        {
            AddErrors(failure);
            if (!failure.Report.Errors.Any()) ModelState.AddModelError("", "That step is not allowed.");
            return View(form);
        }
        TempData["Success"] = message;
        return RedirectToAction(nameof(EditResult), new { contestId });
    }


    // ----- A person's positions --------------------------------------------------------

    [HttpGet("people/{id:int}")]
    public async Task<IActionResult> Person(int id, CancellationToken ct)
    {
        var p = await _queries.PersonAsync(id, ct);
        return p is null ? NotFound() : View(new PersonAdminPageModel(p, _data.GetFormOptions().Parties));
    }

    [HttpGet("people/{id:int}/edit")]
    public async Task<IActionResult> EditPerson(int id, CancellationToken ct)
    {
        var p = await _queries.PersonForEditAsync(id, ct);
        if (p is null) return NotFound();
        return View(new EditPersonPageModel(p, new EditPersonForm { Id = p.Id, FullName = p.FullName, Biography = p.Biography, AdditionalInfo = p.AdditionalInfo }));
    }



    [HttpPost("people/{id:int}/edit")]
    public async Task<IActionResult> EditPerson(int id, EditPersonForm form, CancellationToken ct)
    {
        var p = await _queries.PersonForEditAsync(id, ct);
        if (p is null) return NotFound();
        form.Id = id;

        if (ModelState.IsValid)
        {
            var saved = await _people.UpdateAsync(new EditPersonRequest(id, form.FullName, form.Biography, form.AdditionalInfo, form.ConfirmedNotDuplicate, form.Reason), ct);
            if (saved.Ok)
            {
                var note = "";
                if (form.Photo is { Length: > 0 } photo)
                {
                    using var ms = new MemoryStream();
                    await photo.CopyToAsync(ms, ct);
                    var pr = await _photos.SetPhotoAsync(id, ms.ToArray(), form.PhotoAlt, ct);
                    if (!pr.Ok)
                    {
                        // the text is already saved; keep the person on this page to fix only the photo
                        AddErrors(pr);
                        ModelState.AddModelError("", "The details were saved, but the photo was not.");
                        var fresh = await _queries.PersonForEditAsync(id, ct);
                        return View(new EditPersonPageModel(fresh!, form));
                    }
                    note = " The photo was changed.";
                }
                TempData["Success"] = "Saved." + note + (p.Stage is WorkflowStage.AwaitingVerification or WorkflowStage.Verified or WorkflowStage.Approved
                    ? " Because it had been submitted, it is back to draft and needs to go through review again." : "");
                return RedirectToAction(nameof(Person), new { id });
            }
            AddErrors(saved);
        }
        return View(new EditPersonPageModel(p, form));
    }



    private IActionResult Back(int personId, ServiceResult r, string success)
    {
        TempData["Success"] = r.Ok ? success : (r.FirstError ?? "That could not be saved.");
        return RedirectToAction(nameof(Person), new { id = personId });
    }

    [HttpPost("people/{id:int}/end-appointment")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> EndAppointment(int id, int appointmentId, DateOnly? endDate, string? reason, CancellationToken ct)
    {
        if (endDate is null) return Back(id, Result.Fail(ServiceFailure.Invalid, "end.date", "Enter the end date."), "");
        return Back(id, await _government.EndAppointmentAsync(appointmentId, endDate.Value, reason ?? "", ct), "Appointment ended. The person's other records are unchanged.");
    }


    [HttpPost("people/{id:int}/end-term")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> EndTerm(int id, int termId, DateOnly? endDate, string? reason, CancellationToken ct)
    {
        if (endDate is null) return Back(id, Result.Fail(ServiceFailure.Invalid, "end.date", "Enter the end date."), "");
        return Back(id, await _government.EndTermAsync(termId, endDate.Value, reason ?? "", ct), "Parliamentary term ended. The person's other records are unchanged.");
    }

    [HttpPost("people/{id:int}/change-party")]
    [Authorize(Policy = "perm:" + Permissions.RecordsEnter)]
    public async Task<IActionResult> ChangeParty(int id, ChangePartyForm form, CancellationToken ct)
    {
        if (form.EffectiveFrom is null) return Back(id, Result.Fail(ServiceFailure.Invalid, "party.date", "Enter the date the change took effect."), "");
        var independent = form.PartyId == EfDirectoryReader.IndependentOption;
        var src = form.Source?.Trim();
        var isUrl = src is not null && Uri.TryCreate(src, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https");
        var r = await _government.ChangePartyAsync(id, independent ? null : Int(form.PartyId), independent, form.EffectiveFrom.Value,
            isUrl ? null : src, isUrl ? src : null, ct);
        return Back(id, r, "Party change saved. The earlier party stays on record with its dates.");
    }

    // ----- Import people and positions from Excel ------------------------------------------

    [HttpGet("import")]
    [Authorize(Policy = "perm:" + Permissions.ImportRun)]
    public async Task<IActionResult> Import(CancellationToken ct) => View(new ImportPageModel(await _import.ReferenceAsync(ct)));

    [HttpPost("import")]
    [Authorize(Policy = "perm:" + Permissions.ImportRun)]
    [RequestSizeLimit(ImportService.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, string? mode, CancellationToken ct)
    {
        var reference = await _import.ReferenceAsync(ct);
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Choose an Excel (.xlsx) file first.");
            return View(new ImportPageModel(reference));
        }

        await using var stream = file.OpenReadStream();
        if (mode == "import")
        {
            var done = await _import.ImportAsync(stream, file.Length, file.FileName, ct);
            if (!done.Ok)
            {
                AddErrors(done);
                return View(new ImportPageModel(reference, null, null, file.FileName));
            }
            return View(new ImportPageModel(reference, null, done.Value, file.FileName));
        }

        var check = await _import.CheckAsync(stream, file.Length, ct);
        if (!check.Ok)
        {
            AddErrors(check);
            return View(new ImportPageModel(reference, null, null, file.FileName));
        }
        return View(new ImportPageModel(reference, check.Value, null, file.FileName));
    }







    // ----- Lists ----------------------------------------------------------------------

    [HttpGet("appointments")]
    public async Task<IActionResult> Appointments(string? q, CancellationToken ct) => View(new AppointmentsListModel(q, await _queries.AppointmentsAsync(q, 300, ct)));

    [HttpGet("parliament")]
    public async Task<IActionResult> Parliament(string? q, CancellationToken ct) => View(new TermsListModel(q, await _queries.TermsAsync(q, 300, ct)));

    // ----- Users ----------------------------------------------------------------------

    private async Task<UsersPageModel> UsersModel(AddUserForm f)
    {
        var rows = new List<UserRow>();
        foreach (var u in _users.Users.OrderBy(u => u.Email).ToList())
            rows.Add(new UserRow(u.Id, u.Email ?? u.UserName ?? "", u.DisplayName, u.IsActive, string.Join(", ", await _users.GetRolesAsync(u))));
        return new UsersPageModel(rows, f, Roles.All);
    }


    [HttpGet("users")]
    [Authorize(Policy = "perm:" + Permissions.UsersManage)]
    public async Task<IActionResult> Users() => View(await UsersModel(new AddUserForm()));

    [HttpPost("users")]
    [Authorize(Policy = "perm:" + Permissions.UsersManage)]
    public async Task<IActionResult> Users(AddUserForm form)
    {
        if (ModelState.IsValid && !Roles.All.Contains(form.Role!)) ModelState.AddModelError("", "Select one of the listed roles.");
        if (ModelState.IsValid)
        {
            var user = new ApplicationUser { UserName = form.Email, Email = form.Email, EmailConfirmed = true, DisplayName = form.DisplayName };
            var created = await _users.CreateAsync(user, form.Password!);
            if (created.Succeeded)
            {
                await _users.AddToRoleAsync(user, form.Role!);
                TempData["Success"] = $"User {form.Email} created as {form.Role}.";
                return RedirectToAction(nameof(Users));
            }
            foreach (var e in created.Errors) ModelState.AddModelError("", e.Description);
        }
        form.Password = null;
        return View(await UsersModel(form));
    }


    [HttpPost("users/{id}/toggle")]
    [Authorize(Policy = "perm:" + Permissions.UsersManage)]
    public async Task<IActionResult> ToggleUser(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (u.Id == _users.GetUserId(User)) { TempData["Success"] = "You cannot deactivate your own account."; return RedirectToAction(nameof(Users)); }
        u.IsActive = !u.IsActive;
        await _users.UpdateAsync(u);
        TempData["Success"] = u.IsActive ? "User reactivated." : "User deactivated. They can no longer sign in.";
        return RedirectToAction(nameof(Users));
    }

}





