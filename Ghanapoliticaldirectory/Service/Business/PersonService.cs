using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service.Business;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

public sealed record AddPersonOutcome(int PersonId, bool CreatedNewPerson, IReadOnlyList<string> CreatedRecords, IReadOnlyList<DuplicateMatch> Duplicates);



public sealed record EditPersonRequest(int PersonId, string? FullName, string? Biography, string? AdditionalInfo, bool ConfirmedNotDuplicate, string? Reason);

/// <summary>
/// Saves a person and the positions ticked on the form. There is always exactly ONE Person row; every position,
/// party period or candidacy is its own record with its own dates, linked to that person.
/// </summary>
public sealed class PersonService : ServiceBase
{
    public PersonService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    public async Task<IReadOnlyList<DuplicateMatch>> FindSimilarAsync(string? name, CancellationToken ct = default)
    {
        if (NameNormalizer.Normalize(name).Length < 2) return Array.Empty<DuplicateMatch>();
        // Archived people are included on purpose: an archived record must not be silently re-created.
        var people = await Db.People.AsNoTracking().Select(p => new { p.Id, p.FullName }).ToListAsync(ct);
        return DuplicateDetector.Find(name, people.Select(p => (p.Id, p.FullName)));
    }

    public async Task<ServiceResult<AddPersonOutcome>> AddAsync(AddPersonRequest req, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter))
            return Denied<AddPersonOutcome>("You do not have permission to enter records.");

        var report = AddPersonValidator.Validate(req);

        IReadOnlyList<DuplicateMatch> dups = Array.Empty<DuplicateMatch>();
        if (req.ExistingPersonId is null)
        {
            dups = await FindSimilarAsync(req.FullName, ct);
            if (DuplicateDetector.BlocksCreation(dups, req.ConfirmedNotDuplicate))
                report.Error("person.duplicate",
                    $"\"{dups[0].FullName}\" looks like the same person. Open that record and add the position there, or confirm that this is a different person.",
                    "FullName");
        }

        if (!report.IsValid)
            return Result.Invalid(report, new AddPersonOutcome(0, false, Array.Empty<string>(), dups));

        return await InTransactionAsync(() => AddCoreAsync(req, report, dups, ct), ct);
    }

    private async Task<ServiceResult<AddPersonOutcome>> AddCoreAsync(
        AddPersonRequest req, ValidationReport report, IReadOnlyList<DuplicateMatch> dups, CancellationToken ct)
    {
        var created = new List<string>();
        Person person;
        var isNew = req.ExistingPersonId is null;

        if (req.ExistingPersonId is int existingId)
        {
            var found = await Db.People.FirstOrDefaultAsync(p => p.Id == existingId, ct);
            if (found is null) return Missing<AddPersonOutcome>("That person");
            if (found.IsArchived)
                return Result.Fail<AddPersonOutcome>(ServiceFailure.Conflict, "person.archived", "That person is archived. Restore the record before adding to it.");
            person = found;
        }
        else
        {
            var name = req.FullName!.Trim();
            person = new Person
            {
                FullName = name,
                NormalizedName = NameNormalizer.Normalize(name),
                Biography = Clean(req.Biography),
                AdditionalInfo = Clean(req.AdditionalInfo)
            };
            Db.People.Add(person);
            created.Add("Person");
        }

        var personId = person.Id; // 0 for a new person, which the rules treat as "not yet in any list"
        var affiliations = isNew
            ? new List<PartyAffiliation>()
            : await Db.PartyAffiliations.Where(a => a.PersonId == personId && !a.IsArchived).ToListAsync(ct);
        var periods = affiliations.Select(a => new AffiliationPeriod(a.Id, a.PartyId, new DateRange(a.StartDate, a.EndDate))).ToList();

        // ---- party affiliation
        if (req.PartyId is not null || req.IndependentNoParty)
        {
            if (req.PartyId is int partyId && !await Db.PoliticalParties.AnyAsync(p => p.Id == partyId && !p.IsArchived, ct))
                report.Error("affiliation.party_missing", "The selected party does not exist.", "PartyId");

            var range = new DateRange(req.PartyStart!.Value, null);
            var affReport = PartyAffiliationRules.Validate(range, periods);
            report.AddRange(affReport);

            if (affReport.IsValid && report.IsValid)
            {
                Db.PartyAffiliations.Add(new PartyAffiliation
                {
                    Person = person,
                    PartyId = req.PartyId,
                    StartDate = range.Start
                });
                periods.Add(new AffiliationPeriod(0, req.PartyId, range));
                created.Add(req.PartyId is null ? "Party affiliation (independent)" : "Party affiliation");
            }
        }

        // ---- positions: each becomes its own record
        foreach (var p in req.Positions)
        {
            if (p.Kind == PositionKind.MemberOfParliament)
                await AddTermAsync(person, p, periods, report, created, ct);
            else
                await AddAppointmentAsync(person, p, report, created, ct);
        }

        if (!report.IsValid)
            return Result.Invalid(report, new AddPersonOutcome(0, false, Array.Empty<string>(), dups));

        await Db.SaveChangesAsync(ct);

        Audit(isNew ? "person.created" : "person.records_added", nameof(Person), person.Id,
            $"{(isNew ? "Added" : "Added records for")} {person.FullName}: {string.Join(", ", created)}",
            new { created });
        await Db.SaveChangesAsync(ct);

        return Result.Ok(new AddPersonOutcome(person.Id, isNew, created, dups), report);
    }
    /// <summary>
    /// Changes a person's name, biography or additional information. A draft is simply edited. A record that someone has
    /// already verified or approved goes back to draft, because the reviewer signed off on the old text. A published
    /// profile stays published, but the change needs a reason and the previous text is kept as a revision.
    /// </summary>
    public async Task<ServiceResult> UpdateAsync(EditPersonRequest req, CancellationToken ct = default)
    {
        var person = await Db.People.FirstOrDefaultAsync(p => p.Id == req.PersonId, ct);
        if (person is null) return Missing("That person");
        if (person.IsArchived)
            return Result.Fail(ServiceFailure.Conflict, "person.archived", "That person is archived. Restore the record before editing it.");
        if (!CanEditRecord(person.Stage))
            return Denied("You do not have permission to change this record.");

        var report = new ValidationReport();
        var name = req.FullName?.Trim() ?? "";
        if (name.Length == 0) report.Error("person.name_required", "Enter the person's full name.", "FullName");
        else if (name.Length < 2) report.Error("person.name_too_short", "The full name is too short.", "FullName");
        else if (name.Length > 200) report.Error("person.name_too_long", "The full name must be 200 characters or fewer.", "FullName");
        var bio = Clean(req.Biography);
        var info = Clean(req.AdditionalInfo);
        if (bio is { Length: > 8000 }) report.Error("person.bio_too_long", "The biography must be 8000 characters or fewer.", "Biography");
        if (info is { Length: > 2000 }) report.Error("person.info_too_long", "The additional information must be 2000 characters or fewer.", "AdditionalInfo");

        var reason = Clean(req.Reason);
        var reviewed = person.Stage > WorkflowStage.AwaitingVerification;
        var nameChanged = report.IsValid && NameNormalizer.Normalize(name) != person.NormalizedName;
        var changed = report.IsValid && (name != person.FullName || bio != person.Biography || info != person.AdditionalInfo);
        if (!report.IsValid) return Result.Invalid(report);
        if (!changed) return Result.Ok();

        if (reviewed && reason is null)
            return Result.Invalid(report.Error("person.reason_required", "This profile has already been reviewed. Say why it is being changed; the reason and the previous text are kept in the history.", "Reason"));

        if (nameChanged)
        {
            var others = (await FindSimilarAsync(name, ct)).Where(d => d.PersonId != person.Id).ToList();
            if (DuplicateDetector.BlocksCreation(others, req.ConfirmedNotDuplicate))
                return Result.Invalid(report.Error("person.duplicate",
                    $"\"{others[0].FullName}\" looks like the same person. If this is really a different person, tick the box to confirm and save again.", "FullName"));
        }

        return await InTransactionAsync(async () =>
        {
            var wasStage = person.Stage;
            if (reviewed)
                await AddRevisionAsync(nameof(Person), person.Id,
                    new { person.FullName, person.Biography, person.AdditionalInfo, Stage = wasStage.ToString() }, reason!, ct);

            person.FullName = name;
            person.NormalizedName = NameNormalizer.Normalize(name);
            person.Biography = bio;
            person.AdditionalInfo = info;

            // Verified or approved text has changed, so those sign-offs no longer apply. Published stays published (with the reason on record).
            if (wasStage is WorkflowStage.AwaitingVerification or WorkflowStage.Verified or WorkflowStage.Approved)
                WorkflowStamp.Apply(person, WorkflowAction.ReturnToDraft, WorkflowStage.Draft, User.UserId ?? "system", Now, "Edited after submission");

            Audit("person.updated", nameof(Person), person.Id,
                $"Edited {person.FullName}{(reviewed ? " (reviewed record)" : "")}{(reason is null ? "" : ": " + reason)}",
                new { wasStage = wasStage.ToString(), nameChanged });
            await Db.SaveChangesAsync(ct);
            return Result.Ok();
        }, ct);
    }




    private async Task AddAppointmentAsync(Person person, PositionRequest p, ValidationReport report, List<string> created, CancellationToken ct)
    {
        var position = await ResolvePositionAsync(p, ct);
        var label = position.Title;

        Administration? admin = null;
        if (p.AdministrationId is int aid)
        {
            admin = await Db.Administrations.FirstOrDefaultAsync(a => a.Id == aid && !a.IsArchived, ct);
            if (admin is null) { report.Error("position.administration_missing", $"{label}: the selected administration does not exist.", $"{p.Kind}.AdministrationId"); return; }
        }

        if (p.MinistryId is int mid && !await Db.Ministries.AnyAsync(m => m.Id == mid && !m.IsArchived, ct))
        {
            report.Error("position.ministry_missing", $"{label}: the selected ministry does not exist.", $"{p.Kind}.MinistryId");
            return;
        }

        var status = p.Status ?? (p.End is DateOnly e && e < Today ? AppointmentStatus.Ended : AppointmentStatus.Active);
        var rule = new PositionRule(position.Id, position.Kind, position.Title, position.MaxConcurrentHolders, position.RequiresMinistry, position.RequiresAdministration);

        var ministryId = p.MinistryId;
        var sameScope = position.Id == 0
            ? new List<ExistingHolding>()
            : (await Db.Appointments
                .Where(a => a.PositionId == position.Id && a.MinistryId == ministryId && !a.IsArchived)
                .Select(a => new { a.Id, a.PersonId, a.StartDate, a.EndDate })
                .ToListAsync(ct))
              .Select(a => new ExistingHolding(a.Id, a.PersonId, new DateRange(a.StartDate, a.EndDate)))
              .ToList();

        var check = AppointmentRules.Validate(
            new AppointmentCandidate(person.Id, rule, p.MinistryId, p.AdministrationId, p.Start, p.End, status),
            sameScope,
            admin is null ? null : new AdministrationPeriod(admin.Id, new DateRange(admin.StartDate, admin.EndDate)),
            Today);
        report.AddRange(check);
        if (!check.IsValid) return;

        var source = await ResolveSourceAsync(p.SourceTitle, p.SourceUrl, ct);
        Db.Appointments.Add(new Appointment
        {
            Person = person,
            Position = position,
            AdministrationId = p.AdministrationId,
            MinistryId = p.MinistryId,
            StartDate = p.Start!.Value,
            EndDate = p.End,
            Status = status,
            Source = source,
            EndReason = null
        });
        LinkPersonSource(person, source);
        created.Add(label);
    }

    private async Task AddTermAsync(Person person, PositionRequest p, List<AffiliationPeriod> periods, ValidationReport report, List<string> created, CancellationToken ct)
    {
        var cycleId = p.ElectionCycleId!.Value;
        var seatId = p.ConstituencyId!.Value;

        if (!await Db.ElectionCycles.AnyAsync(c => c.Id == cycleId && !c.IsArchived, ct))
        { report.Error("position.cycle_missing", "Member of Parliament: the selected election cycle does not exist.", "MemberOfParliament.ElectionCycleId"); return; }
        if (!await Db.Constituencies.AnyAsync(c => c.Id == seatId && !c.IsArchived, ct))
        { report.Error("position.constituency_missing", "Member of Parliament: the selected constituency does not exist.", "MemberOfParliament.ConstituencyId"); return; }

        // The party the person stood for at that election: chosen explicitly, else read from a dated affiliation.
        // If neither exists the party is unknown, and we ask rather than guess (and never use today's party).
        int? partyId = p.PartyAtElectionId;
        if (partyId is null && !p.IndependentAtElection)
        {
            var onDate = PartyAffiliationRules.PartyAt(p.Start!.Value, periods);
            if (!onDate.IsKnown)
            {
                report.Error("position.party_required",
                    "Member of Parliament: select the party the person stood for in that election, or 'independent'. It cannot be assumed from their current party.",
                    "MemberOfParliament.PartyAtElectionId");
                return;
            }
            partyId = onDate.PartyId;
        }

        var inSeat = (await Db.ParliamentaryTerms
                .Where(t => t.ConstituencyId == seatId && !t.IsArchived)
                .Select(t => new { t.Id, t.PersonId, t.ConstituencyId, t.StartDate, t.EndDate }).ToListAsync(ct))
            .Select(t => new TermHolding(t.Id, t.PersonId, t.ConstituencyId, new DateRange(t.StartDate, t.EndDate))).ToList();

        var personId = person.Id;
        var forPerson = personId == 0
            ? new List<TermHolding>()
            : (await Db.ParliamentaryTerms
                .Where(t => t.PersonId == personId && !t.IsArchived)
                .Select(t => new { t.Id, t.PersonId, t.ConstituencyId, t.StartDate, t.EndDate }).ToListAsync(ct))
              .Select(t => new TermHolding(t.Id, t.PersonId, t.ConstituencyId, new DateRange(t.StartDate, t.EndDate))).ToList();

        var check = ParliamentaryTermRules.Validate(person.Id, seatId, p.Start, p.End, inSeat, forPerson);
        report.AddRange(check);
        if (!check.IsValid) return;

        var contest = await ContestResolver.GetOrAddAsync(Db, cycleId, ContestType.Parliamentary, seatId, 1, ct);
        var candidate = personId == 0 ? null : await Db.Candidates.FirstOrDefaultAsync(c => c.ElectionContestId == contest.Id && c.PersonId == personId, ct);
        if (candidate is null)
        {
            candidate = new Candidate { ElectionContest = contest, Person = person, PartyId = partyId, Status = CandidateStatus.Contested };
            Db.Candidates.Add(candidate);
        }

        var source = await ResolveSourceAsync(p.SourceTitle, p.SourceUrl, ct);
        Db.ParliamentaryTerms.Add(new ParliamentaryTerm
        {
            Person = person,
            ConstituencyId = seatId,
            ElectionCycleId = cycleId,
            Candidate = candidate,
            StartDate = p.Start!.Value,
            EndDate = p.End,
            Source = source
        });
        LinkPersonSource(person, source);
        created.Add("Candidacy");
        created.Add("Member of Parliament");
    }

    /// <summary>The configured office for this kind. "Other" offices are matched by title and created if new.</summary>
    private async Task<Position> ResolvePositionAsync(PositionRequest p, CancellationToken ct)
    {
        if (p.Kind == PositionKind.Other)
        {
            var title = p.Title!.Trim();
            var lowered = title.ToLower();
            var existing = await Db.Positions.FirstOrDefaultAsync(x => x.Kind == PositionKind.Other && x.Title.ToLower() == lowered, ct);
            if (existing is not null) return existing;
            var made = new Position { Kind = PositionKind.Other, Title = title };
            Db.Positions.Add(made);
            return made;
        }

        var kind = p.Kind;
        var position = await Db.Positions.FirstOrDefaultAsync(x => x.Kind == kind && !x.IsArchived, ct);
        if (position is not null) return position;

        var d = PositionDefaults.For(kind);
        position = new Position
        {
            Kind = kind,
            Title = d.Title,
            MaxConcurrentHolders = d.MaxConcurrentHolders,
            RequiresMinistry = d.RequiresMinistry,
            RequiresAdministration = d.RequiresAdministration
        };
        Db.Positions.Add(position);
        return position;
    }

    private static void LinkPersonSource(Person person, Source? source)
    {
        if (source is null) return;
        if (!person.Sources.Any(s => ReferenceEquals(s.Source, source)))
            person.Sources.Add(new PersonSource { Person = person, Source = source });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Finds or creates the contest a candidate belongs to, so a contest exists exactly once.</summary>
internal static class ContestResolver
{
    public static async Task<ElectionContest> GetOrAddAsync(
        AppDbContext db, int cycleId, ContestType type, int? constituencyId, int round, CancellationToken ct)
    {
        var existing = await db.ElectionContests.FirstOrDefaultAsync(c =>
            c.ElectionCycleId == cycleId && c.ContestType == type && c.ConstituencyId == constituencyId && c.Round == round, ct);
        if (existing is not null) return existing;

        var contest = new ElectionContest { ElectionCycleId = cycleId, ContestType = type, ConstituencyId = constituencyId, Round = round };
        db.ElectionContests.Add(contest);
        return contest;
    }
}
