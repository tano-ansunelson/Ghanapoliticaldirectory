using Ghanapoliticaldirectory.Domain;
using Ghanapoliticaldirectory.Domain.Rules;
using Ghanapoliticaldirectory.Models;
using System.Globalization;

namespace Ghanapoliticaldirectory.Service.Business.Import;

    public class ImportPlanner
    {

    public static readonly string[] Columns =
  {
        "Full Name", "Position", "Start Date", "End Date", "Administration", "Ministry", "Election Year", "Region",
        "Constituency", "Party At Election", "Party", "Party Since", "Title", "Source", "Biography",
        "Person Ref", "Existing Person Id", "Different Person"
    };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "d/M/yyyy", 
        
        "dd/MM/yyyy", "d-M-yyyy",
        "dd-MM-yyyy", "d.M.yyyy",
        "d MMMM yyyy", "d MMM yyyy",
        "MMMM d, yyyy", "yyyy/MM/dd"
    };

    public static ImportPlan Plan(SheetData sheet, ImportLookups lookups, DateOnly today)
    {
        var headerIndex = FindHeader(sheet, out var map);
        var dataRows = headerIndex < 0 ? new List<SheetRow>() : sheet.Rows.Skip(headerIndex + 1).ToList();
        var plan = new ImportPlan { Sheet = sheet.Name, DataRows = dataRows.Count };

        if (headerIndex < 0)
        {
            plan.FileIssues.Add(new ImportIssue(1, "", "The header row was not found. The first row must contain the column names, including \"Full Name\", \"Position\" and \"Start Date\". Download the template and paste your rows under its headings."));
            return plan;
        }
        foreach (var required in new[] { "fullname", "position", "startdate" })
            if (!map.ContainsKey(required))
                plan.FileIssues.Add(new ImportIssue(sheet.Rows[headerIndex].Number, "", $"The column \"{Pretty(required)}\" is missing."));
        if (plan.HasFileProblem) return plan;

        string Cell(SheetRow row, string key) => map.TryGetValue(key, out var i) && i < row.Cells.Count ? row.Cells[i].Trim() : "";

        // ---- group rows into people
        var groups = new List<(string Key, List<SheetRow> Rows)>();
        foreach (var row in dataRows)
        {
            var name = Cell(row, "fullname");
            var reference = Cell(row, "personref");
            if (name.Length == 0)
            {
                plan.FileIssues.Add(new ImportIssue(row.Number, "Full Name", "The Full Name is empty. Every row needs the person's name (repeat it on each of their rows)."));
                continue;
            }
            var key = reference.Length > 0 ? "ref:" + reference.ToLowerInvariant() : "name:" + NameNormalizer.Normalize(name);
            var g = groups.FirstOrDefault(x => x.Key == key);
            if (g.Rows is null) { g = (key, new List<SheetRow>()); groups.Add(g); }
            g.Rows.Add(row);
        }

        var fileHoldings = new Dictionary<string, List<ExistingHolding>>();   // appointments in this file, by position + ministry
        var fileTerms = new List<TermHolding>();
        var nextRecord = 1;
        var nextPerson = -1;

        foreach (var (key, rows) in groups)
        {
            var p = new ImportPersonPlan { Key = key, FullName = Cell(rows[0], "fullname") };
            p.Rows.AddRange(rows.Select(r => r.Number));
            var filePersonId = nextPerson--;
            plan.People.Add(p);

            // ---- person-level values: one value per person, taken from whichever rows fill them in
            string One(string column, string key2)
            {
                var vals = rows.Select(r => (r.Number, Value: Cell(r, key2))).Where(x => x.Value.Length > 0).ToList();
                var distinct = vals.Select(v => v.Value.ToLowerInvariant()).Distinct().ToList();
                if (distinct.Count > 1)
                    p.Issues.Add(new ImportIssue(vals[1].Number, column, $"\"{column}\" has different values on different rows for {p.FullName}. Use one value (or leave the other rows blank)."));
                return vals.Count == 0 ? "" : vals[0].Value;
            }

            if (rows.Select(r => NameNormalizer.Normalize(Cell(r, "fullname"))).Distinct().Count() > 1)
                p.Issues.Add(new ImportIssue(rows[0].Number, "Full Name", $"The rows with Person Ref \"{Cell(rows[0], "personref")}\" use different names. Rows of one person must use the same name."));

            p.Biography = One("Biography", "biography") is { Length: > 0 } bio ? bio : null;
            var existingText = One("Existing Person Id", "existingpersonid");
            var differentText = One("Different Person", "differentperson");
            var partyText = One("Party", "party");
            var sinceText = One("Party Since", "partysince");
            p.ConfirmedNotDuplicate = IsYes(differentText);

            var firstRow = rows[0].Number;

            // existing person
            if (existingText.Length > 0)
            {
                if (!int.TryParse(existingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var eid))
                    p.Issues.Add(new ImportIssue(firstRow, "Existing Person Id", $"\"{existingText}\" is not a number."));
                else
                {
                    var found = lookups.People.FirstOrDefault(x => x.Id == eid);
                    if (found is null) p.Issues.Add(new ImportIssue(firstRow, "Existing Person Id", $"There is no person with Id {eid}."));
                    else if (found.IsArchived) p.Issues.Add(new ImportIssue(firstRow, "Existing Person Id", $"{found.Name} (Id {eid}) is archived. Restore the record before adding to it."));
                    else if (NameNormalizer.Normalize(found.Name) != NameNormalizer.Normalize(p.FullName))
                        p.Issues.Add(new ImportIssue(firstRow, "Existing Person Id", $"Id {eid} is \"{found.Name}\", but the row says \"{p.FullName}\". Check that the Id belongs to this person."));
                    else { p.ExistingPersonId = eid; p.ExistingPersonName = found.Name; }
                }
            }
            else
            {
                var dups = DuplicateDetector.Find(p.FullName, lookups.People.Select(x => (x.Id, x.Name)));
                if (DuplicateDetector.BlocksCreation(dups, p.ConfirmedNotDuplicate))
                    p.Issues.Add(new ImportIssue(firstRow, "Full Name",
                        $"\"{dups[0].FullName}\" (Id {dups[0].PersonId}) looks like the same person. To add this position to that person, put {dups[0].PersonId} in \"Existing Person Id\". If they really are a different person, write Yes in \"Different Person\"."));
            }

            // party affiliation (the person's party, from a stated date)
            if (partyText.Length > 0)
            {
                var party = ResolveParty(partyText, lookups, out var independent, out var perr);
                if (perr is not null) p.Issues.Add(new ImportIssue(firstRow, "Party", perr));
                else { p.PartyId = party?.Id; p.IndependentNoParty = independent; p.PartyLabel = independent ? "Independent" : party!.Abbreviation; }
                if (sinceText.Length == 0) p.Issues.Add(new ImportIssue(firstRow, "Party Since", "Enter the date the party membership began, or leave \"Party\" blank if it is not known. Unknown is not the same as independent."));
            }
            if (sinceText.Length > 0)
            {
                if (TryParseDate(sinceText, out var since, out var derr)) p.PartySince = since;
                else p.Issues.Add(new ImportIssue(firstRow, "Party Since", derr!));
                if (partyText.Length == 0) p.Issues.Add(new ImportIssue(firstRow, "Party", "A \"Party Since\" date was given without a party."));
            }

            // ---- one position per row, earliest first
            var ordered = rows.OrderBy(r => TryParseDate(Cell(r, "startdate"), out var d, out _) ? d : DateOnly.MaxValue).ToList();
            foreach (var row in ordered)
                AddPosition(p, row, Cell, lookups, today, fileHoldings, fileTerms, filePersonId, ref nextRecord);

            // ---- the person-level checks the Add person form makes
            var personCheck = AddPersonValidator.Validate(new AddPersonRequest(
                p.ExistingPersonId, p.FullName, p.Biography, null,
                p.PartyId, p.IndependentNoParty, p.PartySince, p.ConfirmedNotDuplicate, Array.Empty<PositionRequest>()));
            foreach (var e in personCheck.Errors)
                if (e.Field == "PartyStart" && p.Issues.Any(i => i.Column == "Party Since")) continue;   // already reported, in the sheet's own words
                else if (!p.Issues.Any(i => i.Message == e.Message)) p.Issues.Add(new ImportIssue(firstRow, "Full Name", e.Message));

            if (p.Positions.Count == 0 && p.Issues.Count == 0)
                p.Issues.Add(new ImportIssue(firstRow, "Position", "No position was found for this person."));
        }

        return plan;
    }

    // -------------------------------------------------------------------------------------------------------

    private static void AddPosition(ImportPersonPlan p, SheetRow row, Func<SheetRow, string, string> cell, ImportLookups lk, DateOnly today,
        Dictionary<string, List<ExistingHolding>> fileHoldings, List<TermHolding> fileTerms, int filePersonId, ref int nextRecord)
    {
        var n = row.Number;
        var errors = new List<ImportIssue>();
        void Err(string col, string msg) => errors.Add(new ImportIssue(n, col, msg));

        var posText = cell(row, "position");
        if (!TryParsePosition(posText, out var kind))
        {
            Err("Position", posText.Length == 0
                ? "The Position is empty."
                : $"\"{posText}\" is not a position the system knows. Use: President, Vice President, Member of Parliament, Minister, Deputy Minister, Chief of Staff, Speaker, or Other (with a Title).");
            p.Issues.AddRange(errors);
            return;
        }
        var rule = PositionDefaults.For(kind);

        // dates
        DateOnly? start = null, end = null;
        var startText = cell(row, "startdate");
        if (startText.Length == 0) Err("Start Date", $"{rule.Title}: the Start Date is empty. Every position needs a start date.");
        else if (TryParseDate(startText, out var sd, out var sderr)) start = sd; else Err("Start Date", sderr!);
        var endText = cell(row, "enddate");
        if (endText.Length > 0)
        {
            if (TryParseDate(endText, out var ed, out var ederr)) end = ed; else Err("End Date", ederr!);
        }
        if (start is not null && end is not null && end < start) Err("End Date", $"{rule.Title}: the End Date is before the Start Date.");

        // administration / ministry
        int? adminId = null, ministryId = null;
        AdministrationLookup? admin = null;
        var adminText = cell(row, "administration");
        if (adminText.Length > 0)
        {
            var hits = lk.Administrations.Where(a => Same(a.Name, adminText)).ToList();
            if (hits.Count == 1) { admin = hits[0]; adminId = admin.Id; }
            else if (hits.Count > 1) Err("Administration", $"More than one administration is called \"{adminText}\". Rename one of them first.");
            else Err("Administration", $"Administration \"{adminText}\" does not exist. Add it under Admin > Administrations first, or use one of: {Join(lk.Administrations.Select(a => a.Name))}.");
        }

        var ministryText = cell(row, "ministry");
        if (ministryText.Length > 0)
        {
            var hits = lk.Ministries.Where(m => m.AllNames.Any(x => Same(x, ministryText))).ToList();
            if (hits.Count == 1) ministryId = hits[0].Id;
            else if (hits.Count > 1) Err("Ministry", $"\"{ministryText}\" matches more than one ministry. Use the ministry's current name.");
            else Err("Ministry", $"Ministry \"{ministryText}\" does not exist. Add it under Admin > Ministries first. Names must match exactly (capital letters do not matter).");
        }

        // MP columns
        int? cycleId = null, seatId = null, partyAtElection = null;
        var independentAtElection = false;
        string? cycleLabel = null, seatLabel = null;
        if (kind == PositionKind.MemberOfParliament)
        {
            var yearText = cell(row, "electionyear");
            if (yearText.Length == 0) Err("Election Year", "Member of Parliament: enter the Election Year (the year of the election that produced the term, for example 2024).");
            else if (!int.TryParse(yearText.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)) Err("Election Year", $"\"{yearText}\" is not a year.");
            else
            {
                var hits = lk.Cycles.Where(c => c.Year == year).ToList();
                if (hits.Count == 1) { cycleId = hits[0].Id; cycleLabel = hits[0].Name; }
                else if (hits.Count > 1) Err("Election Year", $"More than one election cycle exists for {year}.");
                else Err("Election Year", $"There is no election cycle for {year}. Add it under Admin > Elections first.");
            }

            var seatText = cell(row, "constituency");
            var regionText = cell(row, "region");
            if (seatText.Length == 0) Err("Constituency", "Member of Parliament: enter the Constituency.");
            else
            {
                var hits = lk.Seats.Where(s => Same(s.Name, seatText) && (regionText.Length == 0 || Same(s.Region, regionText))).ToList();
                if (hits.Count == 1) { seatId = hits[0].Id; seatLabel = $"{hits[0].Name} ({hits[0].Region})"; }
                else if (hits.Count > 1) Err("Region", $"\"{seatText}\" exists in more than one region ({Join(hits.Select(h => h.Region))}). Fill in the Region column.");
                else Err("Constituency", regionText.Length > 0 && lk.Seats.Any(s => Same(s.Name, seatText))
                    ? $"\"{seatText}\" is not in the {regionText} region."
                    : $"Constituency \"{seatText}\" does not exist. Check the spelling against Admin > Regions, or add it there.");
            }

            var atText = cell(row, "partyatelection");
            if (atText.Length > 0)
            {
                var party = ResolveParty(atText, lk, out var ind, out var perr);
                if (perr is not null) Err("Party At Election", perr);
                else { partyAtElection = party?.Id; independentAtElection = ind; }
            }
            else if (p.PartySince is null || start is null || p.PartySince > start)
                Err("Party At Election", "Member of Parliament: enter the party this person stood for at that election (or Independent). It cannot be assumed from their party today. You can also fill in \"Party\" and \"Party Since\" if the membership began on or before the start date.");
        }
        else
        {
            if (cell(row, "electionyear").Length > 0 || cell(row, "constituency").Length > 0)
                Err("Position", $"{rule.Title}: Election Year and Constituency only apply to Members of Parliament. Clear them, or change the Position.");
        }

        string? title = null;
        if (kind == PositionKind.Other)
        {
            title = cell(row, "title");
            if (title.Length == 0) Err("Title", "Other public office: enter the position's Title.");
        }

        var source = cell(row, "source");
        string? sourceTitle = null, sourceUrl = null;
        if (source.Length > 0)
        {
            if (AddPersonValidator.IsHttpUrl(source)) sourceUrl = source;
            else if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase) || source.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                Err("Source", "The Source looks like a web address but does not start with http:// or https://.");
            else sourceTitle = source;
        }

        // the same rules the Add person form applies
        if (errors.Count == 0 && start is not null)
        {
            var request = new PositionRequest(kind, title, cycleId, seatId, partyAtElection, independentAtElection, adminId, ministryId,
                start, end, null, sourceTitle, sourceUrl);

            var form = AddPersonValidator.Validate(new AddPersonRequest(1, null, null, null, null, false, null, false, new[] { request }));
            foreach (var e in form.Errors) Err("Position", e.Message);

            if (errors.Count == 0)
            {
                var status = end is DateOnly e2 && e2 < today ? AppointmentStatus.Ended : AppointmentStatus.Active;
                if (kind == PositionKind.MemberOfParliament)
                {
                    var inSeat = fileTerms.Where(t => t.ConstituencyId == seatId!.Value).ToList();
                    var forPerson = fileTerms.Where(t => t.PersonId == filePersonId).ToList();
                    var check = ParliamentaryTermRules.Validate(filePersonId, seatId!.Value, start, end, inSeat, forPerson);
                    foreach (var e in check.Errors) Err("Start Date", e.Message + " (in this file)");
                    if (errors.Count == 0) fileTerms.Add(new TermHolding(nextRecord++, filePersonId, seatId!.Value, new DateRange(start.Value, end)));
                }
                else
                {
                    var scope = $"{kind}|{(kind == PositionKind.Other ? title!.ToLowerInvariant() : "")}|{ministryId}";
                    if (!fileHoldings.TryGetValue(scope, out var list)) fileHoldings[scope] = list = new List<ExistingHolding>();
                    var pr = new PositionRule(0, kind, kind == PositionKind.Other ? title! : rule.Title, rule.MaxConcurrentHolders, rule.RequiresMinistry, rule.RequiresAdministration);
                    var check = AppointmentRules.Validate(
                        new AppointmentCandidate(filePersonId, pr, ministryId, adminId, start, end, status), list,
                        admin is null ? null : new AdministrationPeriod(admin.Id, new DateRange(admin.Start, admin.End)), today);
                    foreach (var e in check.Errors) Err("Start Date", e.Message + " (in this file)");
                    if (errors.Count == 0) list.Add(new ExistingHolding(nextRecord++, filePersonId, new DateRange(start.Value, end)));
                }
            }

            if (errors.Count == 0)
            {
                var desc = Describe(kind, title, ministryText, adminText, seatLabel, cycleLabel, start.Value, end);
                p.Positions.Add(new ImportPosition(n, request, desc));
            }
        }

        p.Issues.AddRange(errors);
    }

    private static string Describe(PositionKind kind, string? title, string ministry, string admin, string? seat, string? cycle, DateOnly start, DateOnly? end)
    {
        var parts = new List<string> { kind == PositionKind.Other ? title! : PositionDefaults.For(kind).Title };
        if (ministry.Length > 0) parts.Add(ministry);
        if (seat is not null) parts.Add(seat + (cycle is null ? "" : $", {cycle}"));
        if (admin.Length > 0) parts.Add(admin);
        parts.Add($"{start:d MMM yyyy} to {(end is null ? "present" : end.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture))}");
        return string.Join(" · ", parts);
    }

    // ---- parsing helpers (public so they can be tested on their own) --------------------------------------

    private static int FindHeader(SheetData sheet, out Dictionary<string, int> map)
    {
        map = new Dictionary<string, int>();
        for (var r = 0; r < Math.Min(sheet.Rows.Count, 15); r++)
        {
            var candidate = new Dictionary<string, int>();
            var cells = sheet.Rows[r].Cells;
            for (var c = 0; c < cells.Count; c++)
            {
                var key = Key(cells[c]);
                if (key.Length > 0 && !candidate.ContainsKey(key)) candidate[key] = c;
            }
            if (candidate.ContainsKey("fullname") && candidate.ContainsKey("position")) { map = candidate; return r; }
        }
        return -1;
    }

    internal static string Key(string header)
    {
        var s = new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return s switch
        {
            "name" => "fullname",
            "start" or "from" or "startdate" or "began" => "startdate",
            "end" or "to" or "until" or "enddate" => "enddate",
            "admin" => "administration",
            "year" or "electioncycle" => "electionyear",
            "seat" => "constituency",
            "partyatthetime" or "partyatelection" => "partyatelection",
            "reference" or "personreference" => "personref",
            "personid" or "existingid" or "existingperson" => "existingpersonid",
            "differentperson" or "notduplicate" or "confirmdifferentperson" => "differentperson",
            "positiontitle" => "title",
            "bio" => "biography",
            _ => s
        };
    }

    private static string Pretty(string key) => key switch { "fullname" => "Full Name", "position" => "Position", "startdate" => "Start Date", _ => key };

    public static bool TryParsePosition(string text, out PositionKind kind)
    {
        kind = PositionKind.Other;
        var k = new string(text.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        switch (k)
        {
            case "president": kind = PositionKind.President; return true;
            case "vicepresident" or "vp": kind = PositionKind.VicePresident; return true;
            case "memberofparliament" or "mp" or "member": kind = PositionKind.MemberOfParliament; return true;
            case "minister": kind = PositionKind.Minister; return true;
            case "deputyminister": kind = PositionKind.DeputyMinister; return true;
            case "chiefofstaff": kind = PositionKind.ChiefOfStaff; return true;
            case "speaker" or "speakerofparliament": kind = PositionKind.Speaker; return true;
            case "other" or "otherpublicoffice" or "otheroffice": kind = PositionKind.Other; return true;
            default: return false;
        }
    }

    /// <summary>Accepts a real Excel date (stored as a serial number) or text such as 2025-01-07, 07/01/2025 (day first) or 7 January 2025.</summary>
    public static bool TryParseDate(string text, out DateOnly date, out string? error)
    {
        date = default; error = null;
        text = text.Trim();
        if (text.Length == 0) { error = "The date is empty."; return false; }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && !text.Contains('/') && !text.Contains('-'))
        {
            if (serial < 1 || serial > 73415) { error = $"\"{text}\" is not a valid date."; return false; }
            date = new DateOnly(1899, 12, 30).AddDays((int)Math.Floor(serial));
        }
        else if (!DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            error = $"\"{text}\" is not a date the system can read. Use a real Excel date, or write it as 2025-01-07, 07/01/2025 (day first) or 7 January 2025.";
            return false;
        }
        if (date.Year < 1900 || date.Year > 2100) { error = $"\"{text}\" gives the year {date.Year}, which looks wrong."; return false; }
        return true;
    }

    private static PartyLookup? ResolveParty(string text, ImportLookups lk, out bool independent, out string? error)
    {
        independent = false; error = null;
        if (Same(text, "independent") || Same(text, "ind")) { independent = true; return null; }
        var hit = lk.Parties.FirstOrDefault(x => Same(x.Abbreviation, text)) ?? lk.Parties.FirstOrDefault(x => Same(x.Name, text));
        if (hit is null) error = $"Party \"{text}\" is not in the system. Use the abbreviation or full name of one of: {Join(lk.Parties.Select(x => x.Abbreviation))}, or write Independent. Add a new party under Admin > Parties first.";
        return hit;
    }

    private static bool IsYes(string s) => Same(s, "yes") || Same(s, "y") || Same(s, "true");

    private static bool Same(string a, string b) =>
        string.Equals(Squash(a), Squash(b), StringComparison.OrdinalIgnoreCase);

    private static string Squash(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Join(IEnumerable<string> items)
    {
        var list = items.Take(12).ToList();
        return list.Count == 0 ? "(none yet)" : string.Join(", ", list.Select(x => $"\"{x}\""));
    }


}

