
using Microsoft.EntityFrameworkCore;
using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;

namespace Ghanapoliticaldirectory.Data.Seeding;

/// <summary>
/// Reference data that is a matter of public record and needed for the forms to work: the 16 regions, the kinds of
/// office, and the registered parties named in the brief. Safe to run repeatedly: it only adds what is missing.
/// It does NOT invent people, constituencies, dates or results.
/// </summary>
public static class ReferenceSeeder
{
    public static readonly string[] RegionNames =
    {
        "Ahafo", "Ashanti", "Bono", "Bono East", "Central", "Eastern", "Greater Accra", "North East",
        "Northern", "Oti", "Savannah", "Upper East", "Upper West", "Volta", "Western", "Western North"
    };

    /// <summary>Electoral Commission region letters (the first letter of its constituency codes).</summary>
    public static readonly Dictionary<string, string> RegionCodes = new()
    {
        ["Western"] = "A",
        ["Central"] = "B",
        ["Greater Accra"] = "C",
        ["Volta"] = "D",
        ["Eastern"] = "E",
        ["Ashanti"] = "F",
        ["Western North"] = "G",
        ["Ahafo"] = "H",
        ["Bono"] = "J",
        ["Bono East"] = "K",
        ["Oti"] = "L",
        ["Northern"] = "M",
        ["Savannah"] = "N",
        ["Upper West"] = "P",
        ["North East"] = "Q",
        ["Upper East"] = "R"
    };

    private static readonly (string Abbreviation, string Name)[] Parties =
    {
        ("CPP", "Convention People's Party"),
        ("NDC", "National Democratic Congress"),
        ("NPP", "New Patriotic Party"),
        ("PNC", "People's National Convention")
    };

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var regions = await db.Regions.ToListAsync(ct);
        foreach (var name in RegionNames.Where(n => regions.All(r => r.Name != n)))
            db.Regions.Add(new Region { Name = name, Code = RegionCodes[name] });
        foreach (var r in regions.Where(r => r.Code == null && RegionCodes.ContainsKey(r.Name)))
            r.Code = RegionCodes[r.Name];

        var kinds = await db.Positions.Select(p => p.Kind).ToListAsync(ct);
        foreach (var kind in Enum.GetValues<PositionKind>().Where(k => k != PositionKind.Other && !kinds.Contains(k)))
        {
            var d = PositionDefaults.For(kind);
            db.Positions.Add(new Position
            {
                Kind = kind,
                Title = d.Title,
                MaxConcurrentHolders = d.MaxConcurrentHolders,
                RequiresMinistry = d.RequiresMinistry,
                RequiresAdministration = d.RequiresAdministration
            });
        }

        var abbreviations = await db.PoliticalParties.Select(p => p.Abbreviation).ToListAsync(ct);
        foreach (var (abbr, name) in Parties.Where(p => !abbreviations.Contains(p.Abbreviation)))
            db.PoliticalParties.Add(new PoliticalParty { Abbreviation = abbr, Name = name });

        await db.SaveChangesAsync(ct);
    }
}
