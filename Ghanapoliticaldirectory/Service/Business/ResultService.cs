using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service.Business;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

public sealed class ResultLineEdit
{
    public int CandidateId { get; set; }
    public string Name { get; set; } = "";
    public string? PartyLabel { get; set; }
    public long? Votes { get; set; }
    public bool IsDeclaredWinner { get; set; }
}

public sealed class ResultEdit
{
    public int ContestId { get; set; }
    public int? ResultId { get; set; }
    public ResultBasis Basis { get; set; } = ResultBasis.Provisional;
    public long? ValidVotes { get; set; }
    public long? RejectedBallots { get; set; }
    public long? TotalVotesCast { get; set; }
    public long? RegisteredVoters { get; set; }
    public DateOnly? DeclaredOn { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? VerificationNotes { get; set; }
    public WorkflowStage? Stage { get; set; }
    public bool HasBeenCorrected { get; set; }
    public List<ResultLineEdit> Lines { get; set; } = new();
}

/// <summary>
/// Results never calculate a winner and never show percentages on their own: the winner is the one the Electoral
/// Commission declared, and corrections keep the old figures as a revision.
/// </summary>
public sealed class ResultService : ServiceBase
{
    public ResultService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    private IQueryable<ElectionContest> Contests() => Db.ElectionContests
        .Include(c => c.Candidates)
        .Include(c => c.Result!).ThenInclude(r => r.Lines)
        .Include(c => c.Result!).ThenInclude(r => r.Sources).ThenInclude(s => s.Source);

    /// <summary>The form's starting point: every candidate in the contest, with saved figures filled in.</summary>
    public async Task<ServiceResult<ResultEdit>> LoadForEditAsync(int contestId, CancellationToken ct = default)
    {
        var contest = await Contests().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contestId, ct);
        if (contest is null) return Missing<ResultEdit>("That contest");

        var candidateIds = contest.Candidates.Where(c => !c.IsArchived).Select(c => c.Id).ToList();
        var names = await Db.Candidates.AsNoTracking()
            .Where(c => candidateIds.Contains(c.Id))
            .Select(c => new { c.Id, Name = c.Person.FullName, Party = c.Party != null ? c.Party.Abbreviation : "Independent" })
            .ToListAsync(ct);

        var result = contest.Result;
        var edit = new ResultEdit
        {
            ContestId = contestId,
            ResultId = result?.Id,
            Basis = result?.Basis ?? ResultBasis.Provisional,
            ValidVotes = result?.ValidVotes,
            RejectedBallots = result?.RejectedBallots,
            TotalVotesCast = result?.TotalVotesCast,
            RegisteredVoters = result?.RegisteredVoters,
            DeclaredOn = result?.DeclaredOn,
            VerificationNotes = result?.VerificationNotes,
            Stage = result?.Stage,
            HasBeenCorrected = result?.HasBeenCorrected ?? false,
            SourceTitle = result?.Sources.Select(s => s.Source.Title).FirstOrDefault(),
            SourceUrl = result?.Sources.Select(s => s.Source.Url).FirstOrDefault()
        };
        foreach (var n in names.OrderBy(n => n.Name))
        {
            var line = result?.Lines.FirstOrDefault(l => l.CandidateId == n.Id);
            edit.Lines.Add(new ResultLineEdit { CandidateId = n.Id, Name = n.Name, PartyLabel = n.Party, Votes = line?.Votes, IsDeclaredWinner = line?.IsDeclaredWinner ?? false });
        }
        return Result.Ok(edit);
    }

    // ------------------------------------------------------------------ draft

    public async Task<ServiceResult<int>> SaveDraftAsync(ResultEdit edit, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");

        return await InTransactionAsync(async () =>
        {
            var contest = await Contests().FirstOrDefaultAsync(c => c.Id == edit.ContestId, ct);
            if (contest is null) return Missing<int>("That contest");

            var result = contest.Result;
            if (result is not null && result.Stage > WorkflowStage.AwaitingVerification)
                return Result.Fail<int>(ServiceFailure.Conflict, "result.locked",
                    $"This result is {WorkflowRules.Label(result.Stage)}. Ask a verifier to return it to draft, or record a correction.");

            var report = ValidateEdit(edit, contest, ValidationMode.Draft, result);
            if (!report.IsValid) return Result.Invalid<int>(report);

            result ??= NewResult(contest);
            result.Stage = WorkflowStage.Draft;
            await ApplyAsync(result, edit, ct);
            await Db.SaveChangesAsync(ct);

            Audit("result.draft_saved", nameof(ElectionResult), result.Id, $"Saved draft result for contest #{contest.Id}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(result.Id, report);
        }, ct);
    }

    // ------------------------------------------------------------------ workflow

    public async Task<ServiceResult<WorkflowStage>> AdvanceAsync(int resultId, WorkflowAction action, string? notes, CancellationToken ct = default)
    {
        return await InTransactionAsync(async () =>
        {
            var result = await Db.ElectionResults
                .Include(r => r.Lines).Include(r => r.Sources)
                .Include(r => r.ElectionContest).ThenInclude(c => c.ElectionCycle)
                .FirstOrDefaultAsync(r => r.Id == resultId, ct);
            if (result is null) return Missing<WorkflowStage>("That result");
            return await AdvanceLoadedAsync(result, action, notes, ct);
        }, ct);
    }

    /// <summary>Also used by the generic workflow service so results follow exactly the same rules.</summary>
    internal async Task<ServiceResult<WorkflowStage>> AdvanceLoadedAsync(ElectionResult result, WorkflowAction action, string? notes, CancellationToken ct)
    {
        if (WorkflowStamp.NeedsReason(action) && string.IsNullOrWhiteSpace(notes))
            return Result.Fail<WorkflowStage>(ServiceFailure.Invalid, "workflow.reason_required", "Give a reason so the person who entered it knows what to fix.");

        var decision = WorkflowRules.Evaluate(WorkflowStamp.Snapshot(result), action, User.UserId ?? "", User.Permissions, Options.RequireDistinctUsers);
        if (!decision.Allowed) return Result.Fail<WorkflowStage>(ServiceFailure.Forbidden, "workflow.denied", decision.Reason!);

        var report = new ValidationReport();

        if (action == WorkflowAction.Verify)
        {
            var contest = result.ElectionContest;
            var edit = ToEdit(result, await SourceTitlesAsync(result, ct));
            report.AddRange(ResultRules.Validate(ToInput(edit, contest.ContestType, contest.Round, hasSource: result.Sources.Count > 0 || !string.IsNullOrWhiteSpace(edit.SourceTitle)), ValidationMode.Verification));
        }

        if (action == WorkflowAction.Publish)
        {
            var blocker = await PublishBlockerAsync(result, ct);
            if (blocker is not null) report.Error("result.publish_blocked", blocker);
        }

        if (!report.IsValid) return Result.Invalid<WorkflowStage>(report);

        var before = result.Stage;
        WorkflowStamp.Apply(result, action, decision.NewStage!.Value, User.UserId!, Now, notes);
        Audit($"result.{action.ToString().ToLowerInvariant()}", nameof(ElectionResult), result.Id,
            $"Result #{result.Id}: {before} → {result.Stage}", new { notes });
        await Db.SaveChangesAsync(ct);
        return Result.Ok(result.Stage, report);
    }

    /// <summary>A result may only go public once what it refers to is public too.</summary>
    private async Task<string?> PublishBlockerAsync(ElectionResult result, CancellationToken ct)
    {
        if (result.ElectionContest.ElectionCycle.Stage != WorkflowStage.Published)
            return "Publish the election cycle first.";

        var ids = result.Lines.Select(l => l.CandidateId).ToList();
        var candidates = await Db.Candidates.Include(c => c.Person).Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var unpublished = candidates.Where(c => c.Stage != WorkflowStage.Published || c.IsArchived || c.Person.Stage != WorkflowStage.Published || c.Person.IsArchived)
            .Select(c => c.Person.FullName).ToList();
        return unpublished.Count == 0 ? null : $"Publish these candidates (and their profiles) first: {string.Join(", ", unpublished)}.";
    }

    // ------------------------------------------------------------------ correction

    /// <summary>
    /// Changes figures that already passed review. The old figures are kept as a revision, a reason and a source
    /// are required, and the public label becomes "Corrected".
    /// </summary>
    public async Task<ServiceResult<int>> CorrectAsync(ResultEdit edit, string reason, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsCorrect)) return Denied<int>("You do not have permission to correct results.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            return Result.Fail<int>(ServiceFailure.Invalid, "result.reason_required", "Explain the correction in at least a sentence.");

        return await InTransactionAsync(async () =>
        {
            var contest = await Contests().FirstOrDefaultAsync(c => c.Id == edit.ContestId, ct);
            var result = contest?.Result;
            if (contest is null || result is null) return Missing<int>("That result");
            if (result.Stage < WorkflowStage.Verified)
                return Result.Fail<int>(ServiceFailure.Conflict, "result.not_reviewed", "This result has not been reviewed yet. Edit the draft instead.");

            var report = ValidateEdit(edit, contest, ValidationMode.Verification, result);
            if (!report.IsValid) return Result.Invalid<int>(report);

            var titles = await SourceTitlesAsync(result, ct);
            await AddRevisionAsync(nameof(ElectionResult), result.Id, ToEdit(result, titles), reason.Trim(), ct);

            await ApplyAsync(result, edit, ct);
            result.HasBeenCorrected = true;
            result.CorrectionNote = reason.Trim();
            await Db.SaveChangesAsync(ct);

            Audit("result.corrected", nameof(ElectionResult), result.Id, $"Corrected result #{result.Id}: {reason.Trim()}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(result.Id, report);
        }, ct);
    }

    // ------------------------------------------------------------------ helpers

    private ElectionResult NewResult(ElectionContest contest)
    {
        var r = new ElectionResult { ElectionContest = contest };
        Db.ElectionResults.Add(r);
        return r;
    }

    private async Task<List<string>> SourceTitlesAsync(ElectionResult result, CancellationToken ct)
    {
        var ids = result.Sources.Select(s => s.SourceId).ToList();
        return await Db.Sources.Where(s => ids.Contains(s.Id)).Select(s => s.Title).ToListAsync(ct);
    }

    private static ResultEdit ToEdit(ElectionResult r, List<string> sourceTitles) => new()
    {
        ContestId = r.ElectionContestId,
        ResultId = r.Id,
        Basis = r.Basis,
        ValidVotes = r.ValidVotes,
        RejectedBallots = r.RejectedBallots,
        TotalVotesCast = r.TotalVotesCast,
        RegisteredVoters = r.RegisteredVoters,
        DeclaredOn = r.DeclaredOn,
        VerificationNotes = r.VerificationNotes,
        Stage = r.Stage,
        HasBeenCorrected = r.HasBeenCorrected,
        SourceTitle = sourceTitles.FirstOrDefault(),
        Lines = r.Lines.Select(l => new ResultLineEdit { CandidateId = l.CandidateId, Votes = l.Votes, IsDeclaredWinner = l.IsDeclaredWinner, Name = "#" + l.CandidateId }).ToList()
    };

    private static ResultInput ToInput(ResultEdit e, ContestType type, int round, bool hasSource) => new(
        type, round, e.Basis,
        e.Lines.Select(l => new ResultLineInput(l.CandidateId.ToString(), string.IsNullOrEmpty(l.Name) ? "candidate " + l.CandidateId : l.Name, l.Votes, l.IsDeclaredWinner)).ToList(),
        e.ValidVotes, e.RejectedBallots, e.TotalVotesCast, e.RegisteredVoters, hasSource);

    private static ValidationReport ValidateEdit(ResultEdit edit, ElectionContest contest, ValidationMode mode, ElectionResult? existing)
    {
        var valid = contest.Candidates.Where(c => !c.IsArchived).Select(c => c.Id).ToHashSet();
        var report = new ValidationReport();
        foreach (var l in edit.Lines.Where(l => !valid.Contains(l.CandidateId)))
            report.Error("result.unknown_candidate", $"{(string.IsNullOrEmpty(l.Name) ? "A candidate" : l.Name)} is not a candidate in this contest.", "Lines");

        var hasSource = !string.IsNullOrWhiteSpace(edit.SourceTitle) || !string.IsNullOrWhiteSpace(edit.SourceUrl) || (existing?.Sources.Count ?? 0) > 0;
        if (!string.IsNullOrWhiteSpace(edit.SourceUrl) && !AddPersonValidator.IsHttpUrl(edit.SourceUrl))
            report.Error("result.source_url_invalid", "The source must be a web address starting with http:// or https://.", "SourceUrl");

        report.AddRange(ResultRules.Validate(ToInput(edit, contest.ContestType, contest.Round, hasSource), mode));
        return report;
    }

    /// <summary>Copies the figures onto the entity: upserts lines by candidate (never removes one) and attaches the source.</summary>
    private async Task ApplyAsync(ElectionResult result, ResultEdit edit, CancellationToken ct)
    {
        result.Basis = edit.Basis;
        result.ValidVotes = edit.ValidVotes;
        result.RejectedBallots = edit.RejectedBallots;
        result.TotalVotesCast = edit.TotalVotesCast;
        result.RegisteredVoters = edit.RegisteredVoters;
        result.DeclaredOn = edit.DeclaredOn;
        result.VerificationNotes = string.IsNullOrWhiteSpace(edit.VerificationNotes) ? null : edit.VerificationNotes.Trim();

        // Moving the winner flag from one candidate to another: clear the old one first, so the database's
        // "one declared winner per result" index is never violated part-way through the save.
        if (result.Id != 0)
        {
            var clearing = result.Lines.Where(x => x.IsDeclaredWinner && !edit.Lines.Any(l => l.CandidateId == x.CandidateId && l.IsDeclaredWinner)).ToList();
            if (clearing.Count > 0)
            {
                foreach (var x in clearing) x.IsDeclaredWinner = false;
                await Db.SaveChangesAsync(ct);
            }
        }

        foreach (var l in edit.Lines)
        {
            var line = result.Lines.FirstOrDefault(x => x.CandidateId == l.CandidateId);
            if (line is null)
            {
                line = new CandidateResult { ElectionResult = result, CandidateId = l.CandidateId };
                result.Lines.Add(line);
            }
            line.Votes = l.Votes;
            line.IsDeclaredWinner = l.IsDeclaredWinner;
        }

        var source = await ResolveSourceAsync(edit.SourceTitle, edit.SourceUrl, ct);
        if (source is not null && !result.Sources.Any(s => ReferenceEquals(s.Source, source) || (source.Id != 0 && s.SourceId == source.Id)))
            result.Sources.Add(new ResultSource { ElectionResult = result, Source = source });
    }
}
