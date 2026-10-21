using Microsoft.EntityFrameworkCore;
using Ghanapoliticaldirectory.Models;

namespace Ghanapoliticaldirectory.Data.Seeding;

/// <summary>
/// Adds the 275 Electoral Commission constituencies under their regions. Safe to run repeatedly: a constituency that
/// already exists in that region with the same name (ignoring capitals) is left alone.
/// Run it after <see cref="ReferenceSeeder"/>, which creates the regions.
/// </summary>
public static class ConstituencySeeder
{
    public static async Task<int> SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var regions = await db.Regions.ToListAsync(ct);
        var have = (await db.Constituencies.ToListAsync(ct))
            .Select(c => (c.RegionId, c.Name.ToLowerInvariant())).ToHashSet();
        var added = 0;

        foreach (var (regionName, name) in ConstituencyData.All)
        {
            var region = regions.FirstOrDefault(r => string.Equals(r.Name, regionName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Region \"{regionName}\" is missing. Run ReferenceSeeder first.");
            if (!have.Add((region.Id, name.ToLowerInvariant()))) continue;

            db.Constituencies.Add(new Constituency { Region = region, Name = name });
            added++;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }
}
