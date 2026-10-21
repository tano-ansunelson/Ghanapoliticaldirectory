using Ghanapoliticaldirectory.Data;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service.Business;
using Microsoft.EntityFrameworkCore;

namespace Ghanapoliticaldirectory.Business;

public sealed record AddCycleRequest(int Year, string? Name, DateOnly? ElectionDate, bool IsDateConfirmed, string? Notes);

public sealed record AdministrationRow(int Id, string Name, DateOnly Start, DateOnly? End, bool IsConfirmed, WorkflowStage Stage, string? Cycle);
public sealed record MinistryEraRow(string Name, DateOnly From, DateOnly? To, string? Note);
public sealed record MinistryRow(int Id, string CurrentName, IReadOnlyList<MinistryEraRow> Names);

/// <summary>
/// Election cycles and constituencies. The seeder only supplies regions, so these must be entered (with sources
/// checked elsewhere) before a Member of Parliament or a candidate can be attached to them.
/// </summary>
public sealed class ReferenceDataService : ServiceBase
{
    public ReferenceDataService(AppDbContext db, ICurrentUser user, IClock clock, WorkflowOptions options)
        : base(db, user, clock, options) { }

    public async Task<ServiceResult<int>> AddCycleAsync(AddCycleRequest r, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");

        var report = new ValidationReport();
        if (r.Year < 1950 || r.Year > 2100) report.Error("cycle.year", "Enter a four-digit election year.", "Year");
        if (r.ElectionDate is { } d && d.Year != r.Year) report.Error("cycle.date_year", "The election date is not in the election year.", "ElectionDate");
        if (r.IsDateConfirmed && r.ElectionDate is null) report.Error("cycle.date_missing", "Enter the date, or untick 'date confirmed'.", "ElectionDate");
        if (!report.IsValid) return Result.Invalid<int>(report);

        return await InTransactionAsync(async () =>
        {
            if (await Db.ElectionCycles.AnyAsync(c => c.Year == r.Year && !c.IsArchived, ct))
                return Result.Fail<int>(ServiceFailure.Conflict, "cycle.duplicate", $"An election cycle for {r.Year} already exists.");

            var cycle = new ElectionCycle
            {
                Year = r.Year,
                Name = string.IsNullOrWhiteSpace(r.Name) ? $"{r.Year} General Election" : r.Name.Trim(),
                ElectionDate = r.ElectionDate,
                IsDateConfirmed = r.IsDateConfirmed,
                Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim()
            };
            Db.ElectionCycles.Add(cycle);
            await Db.SaveChangesAsync(ct);
            Audit("cycle.created", nameof(ElectionCycle), cycle.Id, $"Added election cycle {cycle.Name}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(cycle.Id);
        }, ct);
    }

    public async Task<ServiceResult<int>> AddConstituencyAsync(int regionId, string? name, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");
        var clean = name?.Trim();
        if (string.IsNullOrEmpty(clean))
            return Result.Invalid<int>(new ValidationReport().Error("constituency.name", "Enter the constituency name.", "Name"));

        return await InTransactionAsync(async () =>
        {
            if (!await Db.Regions.AnyAsync(x => x.Id == regionId && !x.IsArchived, ct))
                return Result.Fail<int>(ServiceFailure.NotFound, "constituency.region", "Select a region.");
            var existing = await Db.Constituencies.Where(c => c.RegionId == regionId && !c.IsArchived).Select(c => c.Name).ToListAsync(ct);
            if (existing.Any(n => string.Equals(n, clean, StringComparison.OrdinalIgnoreCase)))
                return Result.Fail<int>(ServiceFailure.Conflict, "constituency.duplicate", $"\"{clean}\" already exists in this region.");

            var c = new Constituency { RegionId = regionId, Name = clean };
            Db.Constituencies.Add(c);
            await Db.SaveChangesAsync(ct);
            Audit("constituency.created", nameof(Constituency), c.Id, $"Added constituency {clean}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(c.Id);
        }, ct);
    }

    public async Task<ServiceResult<int>> AddMinistryAsync(string? name, DateOnly? nameSince, string? description, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");
        var clean = name?.Trim();
        var report = new ValidationReport();
        if (string.IsNullOrEmpty(clean)) report.Error("ministry.name_required", "Enter the ministry's name.", "Name");
        if (nameSince is null) report.Error("ministry.since_required", "Enter the date this name began, so the name is kept with its dates when it changes.", "NameSince");
        if (!report.IsValid) return Result.Invalid<int>(report);

        return await InTransactionAsync(async () =>
        {
            if (await Db.Ministries.AnyAsync(m => m.Name == clean && !m.IsArchived, ct))
                return Result.Fail<int>(ServiceFailure.Conflict, "ministry.duplicate", $"A ministry called \"{clean}\" already exists.");
            var m = new Ministry { Name = clean!, Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() };
            m.Names.Add(new MinistryNameEra { Ministry = m, Name = clean!, ValidFrom = nameSince!.Value });
            Db.Ministries.Add(m);
            await Db.SaveChangesAsync(ct);
            Audit("ministry.created", nameof(Ministry), m.Id, $"Added ministry {clean}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(m.Id);
        }, ct);
    }

    public async Task<IReadOnlyList<AdministrationRow>> ListAdministrationsAsync(CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return Array.Empty<AdministrationRow>();
        var rows = await Db.Administrations.AsNoTracking().Where(a => !a.IsArchived).OrderByDescending(a => a.StartDate)
            .Select(a => new { a.Id, a.Name, a.StartDate, a.EndDate, a.IsConfirmed, a.Stage, Cycle = a.ElectionCycle != null ? a.ElectionCycle.Name : null })
            .ToListAsync(ct);
        return rows.Select(a => new AdministrationRow(a.Id, a.Name, a.StartDate, a.EndDate, a.IsConfirmed, a.Stage, a.Cycle)).ToList();
    }

    public async Task<IReadOnlyList<MinistryRow>> ListMinistriesAsync(CancellationToken ct = default)
    {
        if (!Can(Permissions.AdminAccess)) return Array.Empty<MinistryRow>();
        var list = await Db.Ministries.AsNoTracking().Include(m => m.Names).Where(m => !m.IsArchived).OrderBy(m => m.Name).ToListAsync(ct);
        return list.Select(m => new MinistryRow(m.Id, m.Name,
            m.Names.OrderBy(n => n.ValidFrom).Select(n => new MinistryEraRow(n.Name, n.ValidFrom, n.ValidTo, n.Note)).ToList())).ToList();
    }

    public async Task<ServiceResult<int>> AddPartyAsync(string? name, string? abbreviation, string? description, string? websiteUrl, DateOnly? foundedOn, CancellationToken ct = default)
    {
        if (!Can(Permissions.RecordsEnter)) return Denied<int>("You do not have permission to enter records.");
        var n = name?.Trim(); var a = abbreviation?.Trim();
        var report = new ValidationReport();
        if (string.IsNullOrEmpty(n)) report.Error("party.name", "Enter the party's name.", "Name");
        if (string.IsNullOrEmpty(a)) report.Error("party.abbreviation", "Enter the party's abbreviation.", "Abbreviation");
        if (!string.IsNullOrWhiteSpace(websiteUrl) && !Domain.Rules.AddPersonValidator.IsHttpUrl(websiteUrl))
            report.Error("party.url", "The website must start with http:// or https://.", "WebsiteUrl");
        if (!report.IsValid) return Result.Invalid<int>(report);

        return await InTransactionAsync(async () =>
        {
            var names = await Db.PoliticalParties.Where(p => !p.IsArchived).Select(p => new { p.Name, p.Abbreviation }).ToListAsync(ct);
            if (names.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Abbreviation, a, StringComparison.OrdinalIgnoreCase)))
                return Result.Fail<int>(ServiceFailure.Conflict, "party.duplicate", "A party with that name or abbreviation already exists.");
            var party = new PoliticalParty
            {
                Name = n!,
                Abbreviation = a!,
                FoundedOn = foundedOn,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                WebsiteUrl = string.IsNullOrWhiteSpace(websiteUrl) ? null : websiteUrl.Trim()
            };
            Db.PoliticalParties.Add(party);
            await Db.SaveChangesAsync(ct);
            Audit("party.created", nameof(PoliticalParty), party.Id, $"Added party {a}");
            await Db.SaveChangesAsync(ct);
            return Result.Ok(party.Id);
        }, ct);
    }
}
