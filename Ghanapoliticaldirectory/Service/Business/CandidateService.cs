using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

public sealed record AddCandidateRequest(
    int ElectionCycleId,
    ContestType ContestType,
    int? ConstituencyId,
    int Round,
    int PersonId,
    int? PartyId,
    bool Independent,
    int? RunningMatePersonId,
    int? BallotNumber);


/// <summary>One person standing in a contest: an existing person, or a new name (a draft profile is created for them).</summary>
public sealed record NewCandidate(
    int? ExistingPersonId, string? NewPersonName, bool ConfirmedNotDuplicate,
    int? PartyId, bool Independent,
    int? RunningMateExistingId, string? RunningMateNewName,
    int? BallotNumber);

public sealed record AddCandidatesRequest(int ElectionCycleId, ContestType ContestType, int? ConstituencyId, int Round, IReadOnlyList<NewCandidate> Candidates);

public sealed record AddCandidatesOutcome(int ContestId, int CandidatesAdded, int PeopleCreated);
/// <summary>Candidacies are separate from results and from terms of office: standing in an election proves nothing about winning.</summary>
public sealed class CandidateService : ServiceBase
{
    public CandidateService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    public async Task<ServiceResult<int>> AddAsync(AddCandidateRequest r, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");

        var report = new ValidationReport();
        if (r.ContestType == ContestType.Presidential && r.ConstituencyId is not null)
            report.Error("candidate.constituency_not_allowed", "A presidential contest has no constituency.", "ConstituencyId");
        if (r.ContestType == ContestType.Parliamentary && r.ConstituencyId is null)
            report.Error("candidate.constituency_required", "Select the constituency.", "ConstituencyId");
        if (r.ContestType == ContestType.Parliamentary && r.RunningMatePersonId is not null)
            report.Error("candidate.running_mate_not_allowed", "Only presidential candidates have a running mate.", "RunningMatePersonId");
        if (r.PartyId is null && !r.Independent)
            report.Error("candidate.party_required", "Select the party the candidate stood for, or 'independent'.", "PartyId");
        if (r.PartyId is not null && r.Independent)
            report.Error("candidate.party_conflict", "Choose a party or 'independent', not both.", "PartyId");
        if (r.Round < 1) report.Error("candidate.round", "The round must be 1 or higher.", "Round");
        if (r.RunningMatePersonId == r.PersonId)
            report.Error("candidate.running_mate_self", "A candidate cannot be their own running mate.", "RunningMatePersonId");
        if (!report.IsValid) return Result.Invalid<int>(report);

        return await InTransactionAsync(async () =>
        {

            var rep = new ValidationReport();
            if (!await Db.ElectionCycles.AnyAsync(c => c.Id == r.ElectionCycleId && !c.IsArchived, ct)) rep.Error("candidate.cycle_missing", "The election cycle does not exist.");
            if (!await Db.People.AnyAsync(p => p.Id == r.PersonId && !p.IsArchived, ct)) rep.Error("candidate.person_missing", "The person does not exist.");
            if (r.ConstituencyId is int cid && !await Db.Constituencies.AnyAsync(c => c.Id == cid && !c.IsArchived, ct)) rep.Error("candidate.constituency_missing", "The constituency does not exist.");
            if (r.RunningMatePersonId is int rm && !await Db.People.AnyAsync(p => p.Id == rm && !p.IsArchived, ct)) rep.Error("candidate.running_mate_missing", "The running mate does not exist.");
            if (r.PartyId is int pid && !await Db.PoliticalParties.AnyAsync(p => p.Id == pid && !p.IsArchived, ct)) rep.Error("candidate.party_missing", "The party does not exist.");
            if (!rep.IsValid) return Result.Invalid<int>(rep);

            var contest = await ContestResolver.GetOrAddAsync(Db, r.ElectionCycleId, r.ContestType, r.ConstituencyId, r.Round, ct);
            if (contest.Id != 0 && await Db.Candidates.AnyAsync(c => c.ElectionContestId == contest.Id && c.PersonId == r.PersonId, ct))
                return Result.Fail<int>(ServiceFailure.Conflict, "candidate.duplicate", "This person is already a candidate in this contest.");

            var candidate = new Candidate
            {
                ElectionContest = contest,
                PersonId = r.PersonId,
                PartyId = r.PartyId,
                RunningMatePersonId = r.RunningMatePersonId,
                BallotNumber = r.BallotNumber,
                Status = CandidateStatus.Contested
            };

            Db.Candidates.Add(candidate);
            await Db.SaveChangesAsync(ct);

            Audit("candidate.created", nameof(Candidate), candidate.Id, $"Added candidate #{candidate.Id} to contest #{contest.Id}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(candidate.Id);
        }, ct);
    }


    /// <summary>
    /// Adds several candidates to one contest in a single step (the winner and all the opponents). Each is an existing
    /// person or a new name; new people are created as draft profiles with just a name, so they still go through review.
    /// Either every candidate is saved or none is.
    /// </summary>
    

    public async Task<ServiceResult<AddCandidatesOutcome>> AddManyAsync(AddCandidatesRequest r, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<AddCandidatesOutcome>("You do not have permission to enter records.");

        var report = new ValidationReport();
        if (r.ContestType == ContestType.Presidential && r.ConstituencyId is not null)
            report.Error("candidate.constituency_not_allowed", "A presidential contest has no constituency.", "ConstituencyId");
        if (r.ContestType == ContestType.Parliamentary && r.ConstituencyId is null)
            report.Error("candidate.constituency_required", "Select the constituency.", "ConstituencyId");
        if (r.Round < 1) report.Error("candidate.round", "The round must be 1 or higher.", "Round");
        if (r.Candidates.Count == 0) report.Error("candidate.none", "Add at least one candidate.");

        var newNames = new List<string>();
        for (var i = 0; i < r.Candidates.Count; i++)
        {
            var c = r.Candidates[i];
            var n = $"Candidate {i + 1}";
            var name = c.NewPersonName?.Trim();
            var hasName = !string.IsNullOrEmpty(name);
            if (c.ExistingPersonId is null && !hasName) report.Error("candidate.person_required", $"{n}: choose an existing person or type a new person's name.");
            if (c.ExistingPersonId is not null && hasName) report.Error("candidate.person_both", $"{n}: choose an existing person OR type a new name, not both.");
            if (hasName && name!.Length < 2) report.Error("candidate.name_short", $"{n}: the name is too short.");
            if (hasName && name!.Length > 200) report.Error("candidate.name_long", $"{n}: the name must be 200 characters or fewer.");
            if (hasName) newNames.Add(NameNormalizer.Normalize(name));
            if (c.PartyId is null && !c.Independent) report.Error("candidate.party_required", $"{n}: select the party they stood for, or Independent.");
            if (c.PartyId is not null && c.Independent) report.Error("candidate.party_conflict", $"{n}: choose a party or Independent, not both.");

            var mateName = c.RunningMateNewName?.Trim();
            var hasMate = c.RunningMateExistingId is not null || !string.IsNullOrEmpty(mateName);
            if (r.ContestType == ContestType.Parliamentary && hasMate) report.Error("candidate.running_mate_not_allowed", $"{n}: only presidential candidates have a running mate.");
            if (c.RunningMateExistingId is not null && !string.IsNullOrEmpty(mateName)) report.Error("candidate.mate_both", $"{n}: choose an existing running mate OR type a new name, not both.");
            if (c.RunningMateExistingId is not null && c.RunningMateExistingId == c.ExistingPersonId) report.Error("candidate.running_mate_self", $"{n}: a candidate cannot be their own running mate.");
        }
        if (newNames.GroupBy(x => x).Any(g => g.Key.Length > 0 && g.Count() > 1))
            report.Error("candidate.same_new_name", "The same new name is listed twice. Each person can stand once in a contest.");
        var existingPicked = r.Candidates.Where(c => c.ExistingPersonId is not null).Select(c => c.ExistingPersonId!.Value).ToList();
        if (existingPicked.Distinct().Count() != existingPicked.Count)
            report.Error("candidate.same_person", "The same person is listed twice. Each person can stand once in a contest.");
        if (!report.IsValid) return Result.Invalid<AddCandidatesOutcome>(report);

        return await InTransactionAsync(async () =>
        {
            var rep = new ValidationReport();
            if (!await Db.ElectionCycles.AnyAsync(c => c.Id == r.ElectionCycleId && !c.IsArchived, ct)) rep.Error("candidate.cycle_missing", "The election cycle does not exist.");
            if (r.ConstituencyId is int cid && !await Db.Constituencies.AnyAsync(c => c.Id == cid && !c.IsArchived, ct)) rep.Error("candidate.constituency_missing", "The constituency does not exist.");
            if (!rep.IsValid) return Result.Invalid<AddCandidatesOutcome>(rep);

            var everyone = (await Db.People.AsNoTracking().Select(p => new { p.Id, p.FullName, p.IsArchived }).ToListAsync(ct))
                .ToDictionary(p => p.Id);
            var partyIds = (await Db.PoliticalParties.AsNoTracking().Where(p => !p.IsArchived).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

            var contest = await ContestResolver.GetOrAddAsync(Db, r.ElectionCycleId, r.ContestType, r.ConstituencyId, r.Round, ct);
            var already = contest.Id == 0
                ? new HashSet<int>()
                : (await Db.Candidates.AsNoTracking().Where(c => c.ElectionContestId == contest.Id && !c.IsArchived).Select(c => c.PersonId).ToListAsync(ct)).ToHashSet();

            var created = new List<Person>();
            Person NewPerson(string rawName)
            {
                var p = new Person { FullName = rawName.Trim(), NormalizedName = NameNormalizer.Normalize(rawName) };
                Db.People.Add(p);
                created.Add(p);
                return p;
            }

            var made = new List<Candidate>();
            for (var i = 0; i < r.Candidates.Count; i++)
            {
                var c = r.Candidates[i];
                var n = $"Candidate {i + 1}";
                Person? person = null; int? personId = c.ExistingPersonId;

                if (personId is int pid)
                {
                    if (!everyone.TryGetValue(pid, out var found) || found.IsArchived) { rep.Error("candidate.person_missing", $"{n}: that person does not exist or is archived."); continue; }
                    if (already.Contains(pid)) { rep.Error("candidate.duplicate", $"{n}: {found.FullName} is already a candidate in this contest."); continue; }
                }
                else
                {
                    var name = c.NewPersonName!.Trim();
                    var dups = DuplicateDetector.Find(name, everyone.Values.Select(p => (p.Id, p.FullName)));
                    if (DuplicateDetector.BlocksCreation(dups, c.ConfirmedNotDuplicate))
                    {
                        rep.Error("candidate.duplicate_person", $"{n}: \"{dups[0].FullName}\" is already in the system and looks like the same person. Choose them from the list instead, or tick \"different person\" if they are not the same.");
                        continue;
                    }
                    person = NewPerson(name);
                }

                if (c.PartyId is int party && !partyIds.Contains(party)) { rep.Error("candidate.party_missing", $"{n}: the party does not exist."); continue; }

                Person? mate = null; int? mateId = c.RunningMateExistingId;
                if (mateId is int mid)
                {
                    if (!everyone.TryGetValue(mid, out var m) || m.IsArchived) { rep.Error("candidate.running_mate_missing", $"{n}: the running mate does not exist or is archived."); continue; }
                }
                else if (!string.IsNullOrWhiteSpace(c.RunningMateNewName))
                {
                    var dups = DuplicateDetector.Find(c.RunningMateNewName, everyone.Values.Select(p => (p.Id, p.FullName)));
                    if (DuplicateDetector.BlocksCreation(dups, c.ConfirmedNotDuplicate))
                    {
                        rep.Error("candidate.duplicate_mate", $"{n}: the running mate \"{dups[0].FullName}\" is already in the system. Choose them from the list instead.");
                        continue;
                    }
                    mate = NewPerson(c.RunningMateNewName);
                }

                var cand = new Candidate
                {
                    ElectionContest = contest,
                    PartyId = c.PartyId,
                    BallotNumber = c.BallotNumber,
                    Status = CandidateStatus.Contested
                };
                if (person is not null) cand.Person = person; else cand.PersonId = personId!.Value;
                if (mate is not null) cand.RunningMate = mate; else if (mateId is not null) cand.RunningMatePersonId = mateId;
                Db.Candidates.Add(cand);
                made.Add(cand);
            }
            if (!rep.IsValid) return Result.Invalid<AddCandidatesOutcome>(rep);

            await Db.SaveChangesAsync(ct);
            foreach (var p in created) Audit("person.created", nameof(Person), p.Id, $"Added {p.FullName} as a candidate (name only; add details later)");
            Audit("candidates.added", nameof(ElectionContest), contest.Id, $"Added {made.Count} candidate(s) to contest #{contest.Id}, creating {created.Count} new profile(s)");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(new AddCandidatesOutcome(contest.Id, made.Count, created.Count));
        }, ct);
    }








    public async Task<ServiceResult> SetStatusAsync(int candidateId, CandidateStatus status, CancellationToken ct = default)
    {
        var c = await Db.Candidates.FirstOrDefaultAsync(x => x.Id == candidateId, ct);
        if (c is null) return Missing("The candidate");
        if (!CanEditRecord(c.Stage)) return Denied("You do not have permission to change this record.");

        return await InTransactionAsync(async () =>
        {
            if (c.Stage >= WorkflowStage.Verified)
                await AddRevisionAsync(nameof(Candidate), c.Id, new { c.Status, c.PartyId, c.BallotNumber }, $"Status changed to {status}", ct);
            var old = c.Status;
            c.Status = status;
            Audit("candidate.status", nameof(Candidate), c.Id, $"Candidate #{c.Id} status {old} → {status}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }








}
