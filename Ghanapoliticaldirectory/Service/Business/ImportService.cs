using Ghanapoliticaldirectory.Business;
using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using Ghanapoliticaldirectory.Service.Business.Import;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Service.Business;


/// <summary>What the Import screen shows beside the upload box: the exact names the sheet must use.</summary>
public sealed record ImportReference(
    IReadOnlyList<PartyLookup> Parties,
    IReadOnlyList<AdministrationLookup> Administrations,
    IReadOnlyList<MinistryLookup> Ministries,
    IReadOnlyList<CycleLookup> Cycles,
    IReadOnlyList<string> Regions);
public sealed  class ImportService :ServiceBase
    {
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private readonly PersonService _people;

    public ImportService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options, PersonService people)
        : base(db, user, clock, options) => _people = people;

    private bool Allowed => Can(Permissions.ImportRun) && Can(Permissions.RecordsEnter);

    public async Task<ImportReference> ReferenceAsync(CancellationToken ct = default)
    {
        if (!Allowed) return new ImportReference(Array.Empty<PartyLookup>(), Array.Empty<AdministrationLookup>(), Array.Empty<MinistryLookup>(), Array.Empty<CycleLookup>(), Array.Empty<string>());
        var lk = await LoadLookupsAsync(false, ct);
        var regions = (await Db.Regions.AsNoTracking().Select(r => r.Name).ToListAsync(ct)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return new ImportReference(lk.Parties, lk.Administrations, lk.Ministries, lk.Cycles, regions);
    }


    /// <summary>Reads and checks the file. Saves nothing.</summary>
    public async Task<ServiceResult<ImportPlan>> CheckAsync(Stream file, long length, CancellationToken ct = default)
    {
        if (!Allowed) return Denied<ImportPlan>("You do not have permission to import records.");
        var read = Read(file, length);
        if (read.Error is not null) return Result.Fail<ImportPlan>(ServiceFailure.Invalid, "import.file", read.Error);

        var lookups = await LoadLookupsAsync(true, ct);
        return Result.Ok(ImportPlanner.Plan(read.Sheet!, lookups, Today));
    }

    /// <summary>
    /// Imports every person whose rows all passed the check. A person with any problem is skipped, so the file can be
    /// corrected and the remaining people uploaded again.
    /// </summary>
    public async Task<ServiceResult<ImportOutcome>> ImportAsync(Stream file, long length, string fileName, CancellationToken ct = default)
    {
        var check = await CheckAsync(file, length, ct);
        if (!check.Ok) return Result.Fail<ImportOutcome>(check.Failure == ServiceFailure.None ? ServiceFailure.Invalid : check.Failure, "import.check", check.FirstError ?? "The file could not be checked.");
        var plan = check.Value!;
        if (plan.HasFileProblem)
            return Result.Fail<ImportOutcome>(ServiceFailure.Invalid, "import.file_problem", "The file has problems that must be fixed first: " + plan.FileIssues[0].Message);

        var results = new List<ImportPersonResult>();
        foreach (var person in plan.People)
        {
            ct.ThrowIfCancellationRequested();
            if (!person.IsReady)
            {
                results.Add(new ImportPersonResult(person.FullName, ImportStatus.Skipped, null, 0,
                    person.Issues.Select(i => $"Row {i.Row}, {i.Column}: {i.Message}").ToList()));
                continue;
            }
            results.Add(await ImportPersonAsync(person, ct));
        }

        var outcome = new ImportOutcome(results);
        Audit("import.people", nameof(Person), null,
            $"Import of {Path.GetFileName(fileName)}: {outcome.Imported} imported, {outcome.Partly} partly, {outcome.Failed} failed, {outcome.Skipped} skipped",
            new { file = Path.GetFileName(fileName), outcome.Imported, outcome.Partly, outcome.Failed, outcome.Skipped });
        await Db.SaveChangesAsync(ct);
        return Result.Ok(outcome);
    }


    private async Task<ImportPersonResult> ImportPersonAsync(ImportPersonPlan person, CancellationToken ct)
    {
        var messages = new List<string>();
        int? personId = person.ExistingPersonId;
        var saved = 0;
        var first = true;

        foreach (var position in person.Positions)   // earliest first, one position per call so each is its own record
        {
            var request = new AddPersonRequest(
                personId, personId is null ? person.FullName : null,
                first ? person.Biography : null, null,
                first ? person.PartyId : null, first && person.IndependentNoParty, first ? person.PartySince : null,
                person.ConfirmedNotDuplicate, new[] { position.Request });

            var r = await _people.AddAsync(request, ct);
            if (!r.Ok)
            {
                messages.AddRange(r.Report.Errors.Select(e => $"Row {position.Row}: {e.Message}"));
                if (r.Report.Errors.Count() == 0) messages.Add($"Row {position.Row}: could not be saved.");
                if (saved == 0 && personId is null)
                {
                    messages.Add("Nothing was saved for this person.");
                    return new ImportPersonResult(person.FullName, ImportStatus.Failed, null, 0, messages);
                }
                // the person exists but this position was refused: carry on with the rest, then report it
                continue;
            }
            personId = r.Value!.PersonId;
            saved++;
            first = false;
        }

        if (saved == person.Positions.Count) return new ImportPersonResult(person.FullName, ImportStatus.Imported, personId, saved, messages);
        return new ImportPersonResult(person.FullName, saved == 0 ? ImportStatus.Failed : ImportStatus.PartlyImported, personId, saved, messages);
    }

    private (SheetData? Sheet, string? Error) Read(Stream file, long length)
    {
        if (length <= 0) return (null, "The file is empty.");
        if (length > MaxFileBytes) return (null, "The file is larger than 5 MB. Split it into smaller files.");
        try
        {
            using var copy = new MemoryStream();
            file.CopyTo(copy);
            if (copy.Length > MaxFileBytes) return (null, "The file is larger than 5 MB. Split it into smaller files.");
            copy.Position = 0;
            // an .xlsx is a zip file: check the signature rather than trusting the file name or the browser's type
            var head = new byte[2];
            if (copy.Read(head, 0, 2) < 2 || head[0] != (byte)'P' || head[1] != (byte)'K')
                return (null, "This is not an Excel (.xlsx) file. In Excel, use Save As and choose \"Excel Workbook (*.xlsx)\". Older .xls and .csv files are not accepted.");
            copy.Position = 0;
            return (XlsxReader.Read(copy, "People"), null);
        }
        catch (XlsxFormatException e) { return (null, e.Message); }
    }




    private async Task<ImportLookups> LoadLookupsAsync(bool includePeople, CancellationToken ct)
    {
        var parties = (await Db.PoliticalParties.AsNoTracking().Where(p => !p.IsArchived)
            .Select(p => new { p.Id, p.Abbreviation, p.Name }).ToListAsync(ct))
            .Select(p => new PartyLookup(p.Id, p.Abbreviation, p.Name)).OrderBy(p => p.Abbreviation, StringComparer.OrdinalIgnoreCase).ToList();

        var admins = (await Db.Administrations.AsNoTracking().Where(a => !a.IsArchived)
            .Select(a => new { a.Id, a.Name, a.StartDate, a.EndDate }).ToListAsync(ct))
            .Select(a => new AdministrationLookup(a.Id, a.Name, a.StartDate, a.EndDate)).OrderByDescending(a => a.Start).ToList();

        var ministries = await Db.Ministries.AsNoTracking().Where(m => !m.IsArchived).Select(m => new { m.Id, m.Name }).ToListAsync(ct);
        var eras = await Db.MinistryNameEras.AsNoTracking().Where(e => !e.IsArchived).Select(e => new { e.MinistryId, e.Name }).ToListAsync(ct);
        var ministryRows = ministries
            .Select(m => new MinistryLookup(m.Id, m.Name, eras.Where(e => e.MinistryId == m.Id).Select(e => e.Name).Append(m.Name).Distinct().ToList()))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var cycles = (await Db.ElectionCycles.AsNoTracking().Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Year, c.Name }).ToListAsync(ct))
            .Select(c => new CycleLookup(c.Id, c.Year, c.Name)).OrderByDescending(c => c.Year).ToList();

        var seats = (await Db.Constituencies.AsNoTracking().Where(c => !c.IsArchived)
            .Select(c => new { c.Id, c.Name, Region = c.Region.Name }).ToListAsync(ct))
            .Select(c => new SeatLookup(c.Id, c.Name, c.Region)).ToList();

        var people = includePeople
            ? (await Db.People.AsNoTracking().Select(p => new { p.Id, p.FullName, p.IsArchived }).ToListAsync(ct))
                .Select(p => new PersonLookup(p.Id, p.FullName, p.IsArchived)).ToList()
            : new List<PersonLookup>();

        return new ImportLookups(parties, admins, ministryRows, cycles, seats, people);
    }












}

