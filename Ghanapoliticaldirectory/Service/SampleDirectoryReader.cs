//using Ghanapoliticaldirectory.Models;

//namespace Ghanapoliticaldirectory.Service
//{
//    public sealed class SampleDirectoryReader:IDirectoryReader
//    {

//        // ------------------------------------------------------------------ reference data

//        static readonly PartyRef Cpp = new(3, "CPP", "Convention People's Party");
//        static readonly PartyRef Ndc = new(2, "NDC", "National Democratic Congress");
//        static readonly PartyRef Npp = new(1, "NPP", "New Patriotic Party");
//        static readonly PartyRef Pnc = new(4, "PNC", "People's National Convention");

//        // Alphabetical by abbreviation: no party is ordered ahead of another.
//        static readonly PartyRef[] Parties = { Cpp, Ndc, Npp, Pnc };

//        static readonly string[] First =
//        {
//        "Kofi","Ama","Yaw","Efua","Kwabena","Abena","Nana","Akosua","Mohammed","Esi","Samuel","Adwoa",
//        "Kwame","Afia","Issah","Gifty","Emmanuel","Comfort","Seth","Patience","Joseph","Mavis","Daniel","Linda"
//    };

//        static readonly string[] Last =
//        {
//        "Adjei Darko","Serwaa Boateng","Owusu-Ansah","Nyarko","Amoah","Frimpong","Kwesi Ofori","Tetteh","Issah",
//        "Quartey","Lamptey","Sarpong","Agyeman","Mensah","Osei","Appiah","Ankrah","Bonsu","Danso","Acheampong",
//        "Asare","Gyamfi","Tagoe","Yeboah"
//    };

//        const int PeopleCount = 24;
//        static string Name(int id) => $"{First[(id - 1) % PeopleCount]} {Last[(id - 1) % PeopleCount]}";

//        static readonly Dictionary<int, PartyRef?> PartyOverride = new()
//        {
//            [1] = Ndc,
//            [2] = Ndc,
//            [3] = Ndc,
//            [4] = Ndc,
//            [6] = Ndc,
//            [7] = null,
//            [9] = Npp,
//            [10] = Npp,
//            [13] = Ndc,
//            [14] = Ndc,
//            [17] = Npp,
//            [18] = Npp
//        };

//        static PartyRef? PartyOf(int id) =>
//            PartyOverride.TryGetValue(id, out var p) ? p : new PartyRef?[] { Npp, Ndc, Cpp, Pnc, null }[id % 5];

//        static readonly string[] RegionNames =
//        {
//        "Ahafo","Ashanti","Bono","Bono East","Central","Eastern","Greater Accra","North East",
//        "Northern","Oti","Savannah","Upper East","Upper West","Volta","Western","Western North"
//    };

//        // Illustrative only; the real list is loaded from the Constituencies table.
//        static readonly string[][] ConstituencyNames =
//        {
//        new[] { "Asunafo North", "Asunafo South" },
//        new[] { "Asokwa", "Bantama", "Manhyia North", "Manhyia South", "Subin" },
//        new[] { "Sunyani East", "Berekum East" },
//        new[] { "Techiman South", "Kintampo North" },
//        new[] { "Cape Coast North", "Cape Coast South", "Assin Central" },
//        new[] { "New Juaben South", "Abuakwa North", "Kwahu West" },
//        new[] { "Ablekuma Central", "Odododiodio", "Korle Klottey", "Tema East" },
//        new[] { "Nalerigu/Gambaga", "Walewale" },
//        new[] { "Tamale Central", "Tamale North", "Yendi" },
//        new[] { "Krachi East", "Nkwanta South" },
//        new[] { "Damongo", "Bole/Bamboi" },
//        new[] { "Bolgatanga Central", "Navrongo Central" },
//        new[] { "Wa Central", "Lawra" },
//        new[] { "Ho Central", "Keta", "Ketu South" },
//        new[] { "Takoradi", "Sekondi", "Effia" },
//        new[] { "Sefwi Wiawso", "Bibiani-Anhwiaso-Bekwai" }
//    };

//        record Const(int Id, int RegionId, string Name);

//        static readonly List<Const> AllConstituencies = RegionNames
//            .Select((_, r) => ConstituencyNames[r].Select((n, i) => new Const((r + 1) * 100 + i + 1, r + 1, n)))
//            .SelectMany(x => x)
//            .ToList();

//        static string RegionName(int id) => RegionNames[id - 1];

//        // Administrations, newest first. Election cycles and administrations are separate records.
//        static readonly int[] PresidentIds = { 2, 9, 13, 17 };
//        static readonly int[] VicePresidentIds = { 3, 10, 14, 18 };
//        static readonly DateOnly[] AdminStarts = { new(2025, 1, 7), new(2017, 1, 7), new(2009, 1, 7), new(2001, 1, 7) };
//        static readonly DateOnly?[] AdminEnds = { null, new DateOnly(2025, 1, 6), new DateOnly(2017, 1, 6), new DateOnly(2009, 1, 6) };
//        static readonly int[] AdminElectionYears = { 2024, 2016, 2008, 2000 };

//        static readonly AdministrationSummary[] Admins = Enumerable.Range(0, 4).Select(a =>
//            new AdministrationSummary(
//                a + 1,
//                $"Administration beginning {AdminStarts[a].Year}",
//                AdminStarts[a],
//                AdminEnds[a],
//                Name(PresidentIds[a]),
//                Name(VicePresidentIds[a]),
//                AdminElectionYears[a],
//                IsCurrent: a == 0,
//                IsDraft: false)).ToArray();

//        static PartyRef? AdminParty(int aIdx) => PartyOf(PresidentIds[aIdx]);

//        // Election cycles, newest first.
//        static readonly ElectionCycleSummary[] Cycles =
//        {
//        new(3, 2028, "2028 General Election", ElectionPhase.CandidatesPublished, true, ResultStatus.NotPublished, ResultStatus.NotPublished),
//        new(2, 2024, "2024 General Election", ElectionPhase.Completed, true, ResultStatus.OfficiallyDeclared, ResultStatus.Verified),
//        new(1, 2020, "2020 General Election", ElectionPhase.Completed, true, ResultStatus.Corrected, ResultStatus.OfficiallyDeclared)
//    };

//        static readonly (int Id, string Name, string Description)[] MinistryList =
//        {
//        (1, "Ministry of Finance", "Sample description. The official mandate text and website are loaded from the ministry record."),
//        (2, "Ministry of Health", "Sample description. The official mandate text and website are loaded from the ministry record."),
//        (3, "Ministry of Defence", "Sample description. The official mandate text and website are loaded from the ministry record."),
//        (4, "Ministry of Agriculture", "Sample description. The official mandate text and website are loaded from the ministry record."),
//        (5, "Ministry of Education", "Sample description. The official mandate text and website are loaded from the ministry record."),
//        (6, "Ministry of Communication and Digital Technology", "Sample description. This ministry has had several names; earlier names are preserved below.")
//    };

//        static readonly (int Id, string Title, string Description)[] OfficeList =
//        {
//        (1, "Chief of Staff", "Sample description of a public office that is not part of any ministry."),
//        (2, "Attorney-General", "Sample description of a public office that is not part of any ministry."),
//        (3, "Speaker of Parliament", "Sample description of a public office that is not part of any ministry."),
//        (4, "Head of the Civil Service", "Sample description of a public office that is not part of any ministry.")
//    };

//        // ------------------------------------------------------------------ helpers

//        static (IReadOnlyList<T> Items, PagedInfo Info) Page<T>(IEnumerable<T> source, int page, int size)
//        {
//            var list = source.ToList();
//            var info = new PagedInfo(Math.Max(1, page), size, list.Count);
//            var p = Math.Min(info.Page, info.TotalPages);
//            info = info with { Page = p };
//            return (list.Skip((p - 1) * size).Take(size).ToList(), info);
//        }

//        static decimal Pct(long votes, long valid) => Math.Round(votes * 100m / valid, 2);

//        static PersonCardModel Card(int id, string position, PartyRef? party, string? context = null) =>
//            new(id, Name(id), null, position, party, context);

//        static OfficeholderRow Holder(int mIdx, int aIdx, int k, string role)
//        {
//            var id = ((mIdx * 4 + aIdx * 7 + k) % PeopleCount) + 1;
//            var party = k == 2 ? null : AdminParty(aIdx);
//            var start = AdminStarts[aIdx].AddDays(k == 0 ? 25 : 40);
//            return new OfficeholderRow(Card(id, role, party), role, start, AdminEnds[aIdx]);
//        }

//        static PersonCardModel OfficeCard(int officeIdx, int aIdx)
//        {
//            var id = ((officeIdx * 5 + aIdx * 7 + 11) % PeopleCount) + 1;
//            var party = OfficeList[officeIdx].Title is "Attorney-General" or "Head of the Civil Service" ? null : AdminParty(aIdx);
//            return Card(id, OfficeList[officeIdx].Title, party);
//        }

//        static string MinistryNameFor(int ministryId, DateOnly on) =>
//            NameEras(ministryId).FirstOrDefault(e => e.Start <= on && (e.End is null || e.End >= on))?.Name
//            ?? MinistryList[ministryId - 1].Name;

//        static List<MinistryNameEra> NameEras(int ministryId) => ministryId == 6
//            ? new List<MinistryNameEra>
//            {
//            new("Ministry of Communications", new DateOnly(2001, 1, 7), new DateOnly(2016, 12, 31), "Sample earlier name"),
//            new("Ministry of Communication", new DateOnly(2017, 1, 1), new DateOnly(2025, 1, 6), "Sample earlier name"),
//            new("Ministry of Communication and Digital Technology", new DateOnly(2025, 1, 7), null, "Current name; earlier names are preserved, not overwritten")
//            }
//            : new List<MinistryNameEra> { new(MinistryList[ministryId - 1].Name, new DateOnly(2001, 1, 7), null, null) };

//        static SourceRef Src(string title, string path, DateOnly published, string status = "Verified against source") =>
//            new(title, "https://example.org/" + path, published, new DateOnly(2026, 9, 30), status);

//        // ------------------------------------------------------------------ public site

//        public HomeViewModel GetHome()
//        {
//            var gov = new List<PersonCardModel>
//        {
//            Card(PresidentIds[0], "President", AdminParty(0)),
//            Card(VicePresidentIds[0], "Vice President", AdminParty(0)),
//            Holder(0, 0, 0, "Minister of Finance").Person,
//            Holder(1, 0, 0, "Minister of Health").Person,
//            OfficeCard(1, 0),
//            OfficeCard(0, 0)
//        };
//            return new HomeViewModel(Cycles, Cycles[0], Admins[0], gov, Admins);
//        }

//        public PersonProfileModel? GetPerson(int id)
//        {
//            if (id < 1 || id > PeopleCount) return null;
//            var party = PartyOf(id);
//            var sources = new List<SourceRef>
//        {
//            Src("Parliament of Ghana: members directory (sample reference)", "parliament/members", new DateOnly(2025, 2, 1)),
//            Src("Government announcement of appointments (sample reference)", "gov/appointments", new DateOnly(2025, 2, 3)),
//            Src("Electoral Commission: constituency results (sample reference)", "ec/constituency-results", new DateOnly(2024, 12, 12))
//        };

//            if (id == 1)
//            {
//                var ablekuma = AllConstituencies.First(c => c.Name == "Ablekuma Central");
//                return new PersonProfileModel(
//                    1, Name(1), null,
//                    "SAMPLE BIOGRAPHY. This paragraph stands in for a verified biography written from official sources. " +
//                    "It explains who the person is, their background and their public service, in neutral language.",
//                    new[]
//                    {
//                    new PartyAffiliation(null, new DateOnly(2008, 3, 1), new DateOnly(2012, 6, 30)),
//                    new PartyAffiliation(Ndc, new DateOnly(2012, 7, 1), null)
//                    },
//                    new[]
//                    {
//                    new ServiceRecord("Minister", "Minister of Finance", MinistryNameFor(1, new DateOnly(2025, 2, 1)),
//                        Admins[0].Name, new DateOnly(2025, 2, 1), null, "Active", Ndc,
//                        new Link("Ministry of Finance", "/ministries?ministryId=1&administrationId=1"),
//                        sources[1]),
//                    new ServiceRecord("Member of Parliament", "Member of Parliament", $"{ablekuma.Name} · {RegionName(7)} Region",
//                        null, new DateOnly(2025, 1, 7), null, "Active", Ndc,
//                        new Link("Parliament directory", $"/parliament?regionId=7&constituencyId={ablekuma.Id}"),
//                        sources[0]),
//                    new ServiceRecord("Member of Parliament", "Member of Parliament", $"{ablekuma.Name} · {RegionName(7)} Region",
//                        null, new DateOnly(2021, 1, 7), new DateOnly(2025, 1, 6), "Ended", Ndc,
//                        new Link("Parliament directory", $"/parliament?regionId=7&constituencyId={ablekuma.Id}"),
//                        sources[0]),
//                    new ServiceRecord("Other public office", "Board member, [sample public body]", null,
//                        null, new DateOnly(2014, 5, 1), new DateOnly(2018, 4, 30), "Ended", null, null, null)
//                    },
//                    new[]
//                    {
//                    new ElectionHistoryItem(2024, "Parliamentary", "Ablekuma Central", Ndc, ResultStatus.Verified, "Elected (sample)"),
//                    new ElectionHistoryItem(2020, "Parliamentary", "Ablekuma Central", Ndc, ResultStatus.OfficiallyDeclared, "Elected (sample)")
//                    },
//                    sources,
//                    new[]
//                    {
//                    new Link("Ministries › Ministry of Finance (Administration beginning 2025)", "/ministries?ministryId=1&administrationId=1"),
//                    new Link("Parliament › Ablekuma Central", $"/parliament?regionId=7&constituencyId={ablekuma.Id}"),
//                    new Link("Presidency › Cabinet (Administration beginning 2025)", "/presidency"),
//                    new Link("Elections › 2024 Parliamentary · Ablekuma Central", $"/elections/parliamentary?year=2024&regionId=7&constituencyId={ablekuma.Id}")
//                    });
//            }

//            var pIdx = Array.IndexOf(PresidentIds, id);
//            var vIdx = Array.IndexOf(VicePresidentIds, id);
//            var records = new List<ServiceRecord>();
//            if (pIdx >= 0)
//                records.Add(new ServiceRecord("President", "President of the Republic", null, Admins[pIdx].Name,
//                    AdminStarts[pIdx], AdminEnds[pIdx], pIdx == 0 ? "Active" : "Ended", party,
//                    new Link("Presidency", "/presidency"), sources[1]));
//            else if (vIdx >= 0)
//                records.Add(new ServiceRecord("Vice President", "Vice President", null, Admins[vIdx].Name,
//                    AdminStarts[vIdx], AdminEnds[vIdx], vIdx == 0 ? "Active" : "Ended", party,
//                    new Link("Presidency", "/presidency"), sources[1]));
//            else
//            {
//                var c = AllConstituencies[(id * 3) % AllConstituencies.Count];
//                records.Add(new ServiceRecord("Member of Parliament", "Member of Parliament", $"{c.Name} · {RegionName(c.RegionId)} Region",
//                    null, new DateOnly(2021, 1, 7), new DateOnly(2025, 1, 6), "Ended", party,
//                    new Link("Parliament directory", $"/parliament?regionId={c.RegionId}&constituencyId={c.Id}"), sources[0]));
//            }

//            return new PersonProfileModel(
//                id, Name(id), null,
//                "SAMPLE BIOGRAPHY. Replace with a verified biography prepared from official sources.",
//                new[] { new PartyAffiliation(party, new DateOnly(2010, 1, 1), null) },
//                records,
//                Array.Empty<ElectionHistoryItem>(),
//                sources.Take(2).ToList(),
//                records.Where(r => r.RelatedPage is not null)
//                       .Select(r => new Link($"{r.Kind} record", r.RelatedPage!.Url)).ToList());
//        }

//        public IReadOnlyList<PresidencyRow> GetPresidency() =>
//            Enumerable.Range(0, 4).Select(a => new PresidencyRow(
//                Admins[a],
//                Card(PresidentIds[a], "President", AdminParty(a)),
//                Card(VicePresidentIds[a], "Vice President", AdminParty(a)))).ToList();

//        public IReadOnlyList<PartyRef> GetParties() => Parties;

//        IEnumerable<PartyPersonRow> AllPartyRows()
//        {
//            var kinds = new[] { "Member of Parliament", "Minister", "Deputy Minister", "Member of Parliament", "Parliamentary candidate", "Presidential candidate" };
//            for (var id = 1; id <= PeopleCount; id++)
//            {
//                for (var k = 0; k < 2; k++)
//                {
//                    var aIdx = (id + k) % 4;
//                    var position = kinds[(id + k * 2) % kinds.Length];
//                    var region = position == "Presidential candidate" ? (int?)null : ((id * 3 + k) % 16) + 1;
//                    var cons = region is null ? null : ConstituencyNames[region.Value - 1][id % ConstituencyNames[region.Value - 1].Length];
//                    var party = PartyOf(id);
//                    var card = Card(id, position, party, Admins[aIdx].Name);
//                    yield return new PartyPersonRow(card, position, Admins[aIdx].Name,
//                        region is null ? null : RegionName(region.Value), cons,
//                        Admins[aIdx].ElectionYear, AdminStarts[aIdx], AdminEnds[aIdx], party);
//                }
//            }
//        }

//        public PartyPageModel? GetParty(int partyId, PartyFilter filter)
//        {
//            var party = Parties.FirstOrDefault(p => p.Id == partyId);
//            if (party is null) return null;

//            var q = AllPartyRows().Where(r => r.PartyAtTime?.Id == partyId);
//            if (filter.Year is not null) q = q.Where(r => r.ElectionYear == filter.Year);
//            if (filter.Position is { Length: > 0 }) q = q.Where(r => r.Position == filter.Position);
//            if (filter.RegionId is not null) q = q.Where(r => r.Region == RegionName(filter.RegionId.Value));
//            if (filter.AdministrationId is not null) q = q.Where(r => r.Administration == Admins[filter.AdministrationId.Value - 1].Name);

//            var (rows, info) = Page(q.OrderBy(r => r.Person.FullName).ThenByDescending(r => r.Start), filter.Page, 8);

//            var history = Cycles.Select((c, i) =>
//            {
//                var slot = PresidentialSlots(c).FirstOrDefault(s => s.Party?.Id == partyId);
//                return new PartyElectionRow(c.Year,
//                    slot is null ? "No candidate recorded" : Name(slot.PersonId),
//                    c.PresidentialStatus);
//            }).ToList();

//            return new PartyPageModel(
//                party,
//                "Sample description. The party's description, official website and logo are loaded from the party record.",
//                "https://example.org/party-website",
//                filter,
//                Parties,
//                Cycles.Select(c => new Option(c.Year.ToString(), c.Year.ToString())).ToList(),
//                Admins.Select(a => new Option(a.Id.ToString(), a.Name)).ToList(),
//                new[] { "Member of Parliament", "Minister", "Deputy Minister", "Parliamentary candidate", "Presidential candidate" }
//                    .Select(p => new Option(p, p)).ToList(),
//                RegionNames.Select((n, i) => new Option((i + 1).ToString(), n)).ToList(),
//                rows, info, history);
//        }

//        // ---- Elections

//        record Slot(PartyRef? Party, int PersonId, int? RunningMateId);

//        static IReadOnlyList<Slot> PresidentialSlots(ElectionCycleSummary c) => c.Year switch
//        {
//            2028 => new[] { new Slot(Ndc, 4, 6), new Slot(Npp, 5, 8), new Slot(Cpp, 12, 15), new Slot(null, 20, null) },
//            2024 => new[] { new Slot(Ndc, 2, 3), new Slot(Npp, 11, 12), new Slot(Cpp, 16, 15), new Slot(null, 19, null) },
//            _ => new[] { new Slot(Npp, 9, 10), new Slot(Ndc, 13, 14), new Slot(Cpp, 16, 21), new Slot(null, 22, null) }
//        };

//        static long[] PresidentialVotes(ElectionCycleSummary c) => c.Year switch
//        {
//            2024 => new long[] { 5_200_000, 4_500_000, 200_000, 100_000 },
//            2020 => new long[] { 5_000_000, 4_300_000, 150_000, 50_000 },
//            _ => Array.Empty<long>()
//        };

//        static bool PercentagesAllowed(ResultStatus s) =>
//            s is ResultStatus.Verified or ResultStatus.OfficiallyDeclared or ResultStatus.Corrected or ResultStatus.Published;

//        public PresidentialPageModel GetPresidential(int? year)
//        {
//            var cycle = Cycles.FirstOrDefault(c => c.Year == year) ?? Cycles[0];
//            var votes = PresidentialVotes(cycle);
//            var status = cycle.PresidentialStatus;
//            var valid = votes.Sum();

//            var candidates = PresidentialSlots(cycle).Select((s, i) => new CandidateModel(
//                s.PersonId, Name(s.PersonId), null, s.Party,
//                cycle.Phase == ElectionPhase.Completed ? "Contested" : "Candidate",
//                s.RunningMateId is null ? null : Name(s.RunningMateId.Value),
//                "SAMPLE BIOGRAPHY. Replace with a verified summary prepared from official sources.",
//                votes.Length > i ? votes[i] : null,
//                votes.Length > i && PercentagesAllowed(status) ? Pct(votes[i], valid) : null,
//                votes.Length > 0 && i == 0 && status is ResultStatus.OfficiallyDeclared or ResultStatus.Corrected))
//                .OrderBy(c => c.FullName).ToList();

//            ContestResult? result = null;
//            if (status != ResultStatus.NotPublished)
//            {
//                var rejected = cycle.Year == 2024 ? 120_000L : 95_000L;
//                var corrected = status == ResultStatus.Corrected;
//                result = new ContestResult(
//                    status,
//                    new ResultTotals(valid, rejected, valid + rejected, cycle.Year == 2024 ? 18_500_000L : 17_000_000L),
//                    new[]
//                    {
//                    Src("Electoral Commission: declaration of presidential results (sample reference)", $"ec/presidential-{cycle.Year}", new DateOnly(cycle.Year, 12, 10)),
//                    Src("Electoral Commission: results by region (sample reference)", $"ec/presidential-{cycle.Year}-regions", new DateOnly(cycle.Year, 12, 11))
//                    },
//                    new DateOnly(cycle.Year, 12, 10),
//                    new DateOnly(cycle.Year, 12, 11),
//                    corrected ? "Sample correction note: one candidate's total was amended after an official notice from the Electoral Commission. The earlier published figure is kept in the change history." : null,
//                    corrected
//                        ? new[]
//                        {
//                        new ChangeLogItem(new DateOnly(cycle.Year, 12, 11), "Result published (officially declared)."),
//                        new ChangeLogItem(new DateOnly(cycle.Year + 1, 3, 2), "Corrected: candidate total amended following an official notice. Previous value retained in the audit log.")
//                        }
//                        : new[] { new ChangeLogItem(new DateOnly(cycle.Year, 12, 11), "Result published.") });
//            }

//            return new PresidentialPageModel(Cycles, cycle, candidates, result);
//        }

//        public IReadOnlyList<Option> GetConstituencies(int regionId) =>
//            AllConstituencies.Where(c => c.RegionId == regionId)
//                .OrderBy(c => c.Name)
//                .Select(c => new Option(c.Id.ToString(), c.Name)).ToList();

//        public ParliamentaryPageModel GetParliamentary(int? year, int? regionId, int? constituencyId)
//        {
//            var cycle = Cycles.FirstOrDefault(c => c.Year == year) ?? Cycles[0];
//            var regions = RegionNames.Select((n, i) => new Option((i + 1).ToString(), n)).ToList();
//            var constituencies = regionId is null ? new List<Option>() : GetConstituencies(regionId.Value).ToList();

//            var cons = constituencyId is null ? null : AllConstituencies.FirstOrDefault(c => c.Id == constituencyId && c.RegionId == regionId);
//            if (cons is null)
//                return new ParliamentaryPageModel(Cycles, cycle, regions, constituencies, regionId, null,
//                    regionId is null ? null : RegionName(regionId.Value), null, Array.Empty<CandidateModel>(), null);

//            ResultStatus status;
//            if (cycle.ParliamentaryStatus == ResultStatus.NotPublished) status = ResultStatus.NotPublished;
//            else if (cycle.Year == 2024) status = (cons.Id % 3) switch { 0 => ResultStatus.OfficiallyDeclared, 1 => ResultStatus.Verified, _ => ResultStatus.Provisional };
//            else status = ResultStatus.OfficiallyDeclared;

//            var scale = 40_000L + (cons.Id % 7) * 3_000L;
//            var shares = new[] { 0.50m, 0.42m, 0.05m };
//            var votes = new long[4];
//            for (var i = 0; i < 3; i++) votes[i] = (long)(scale * shares[i]);
//            votes[3] = scale - votes[0] - votes[1] - votes[2];

//            var order = new PartyRef?[] { Ndc, Npp, Cpp, null };
//            var candidates = Enumerable.Range(0, 4).Select(k =>
//            {
//                var personId = ((cons.Id * 7 + k * 5) % PeopleCount) + 1;
//                var hasResult = status != ResultStatus.NotPublished;
//                return new CandidateModel(
//                    personId, Name(personId), null, order[(k + cons.Id) % 4],
//                    cycle.Phase == ElectionPhase.Completed ? "Contested" : "Candidate", null,
//                    "SAMPLE BIOGRAPHY.",
//                    hasResult ? votes[k] : null,
//                    hasResult && PercentagesAllowed(status) ? Pct(votes[k], scale) : null,
//                    hasResult && k == 0 && status == ResultStatus.OfficiallyDeclared);
//            }).OrderBy(c => c.FullName).ToList();

//            ContestResult? result = status == ResultStatus.NotPublished ? null : new ContestResult(
//                status,
//                new ResultTotals(scale, scale / 50, scale + scale / 50, scale * 2),
//                new[] { Src($"Electoral Commission: {cons.Name} constituency result (sample reference)", $"ec/{cycle.Year}/{cons.Id}", new DateOnly(cycle.Year, 12, 9),
//                status == ResultStatus.Provisional ? "Provisional: not yet verified" : "Verified against source") },
//                status == ResultStatus.Provisional ? null : new DateOnly(cycle.Year, 12, 10),
//                status == ResultStatus.OfficiallyDeclared ? new DateOnly(cycle.Year, 12, 11) : null,
//                null,
//                new[] { new ChangeLogItem(new DateOnly(cycle.Year, 12, 9), "Result entered by data-entry user.") });

//            return new ParliamentaryPageModel(Cycles, cycle, regions, constituencies, regionId, cons.Id,
//                RegionName(cons.RegionId), cons.Name, candidates, result);
//        }

//        // ---- Parliament directory

//        static readonly string[] TermLabels = { "2025–2029", "2021–2025", "2017–2021" };

//        public ParliamentPageModel GetParliament(ParliamentFilter filter)
//        {
//            var rows = new List<MpRow>();
//            foreach (var c in AllConstituencies.OrderBy(c => RegionName(c.RegionId)).ThenBy(c => c.Name))
//            {
//                for (var t = 0; t < 3; t++)
//                {
//                    var personId = c.Id == 701 && t < 2 ? 1 : ((c.Id * 5 + t * 3) % PeopleCount) + 1;
//                    var party = new PartyRef?[] { Ndc, Npp, Cpp, null }[(c.Id + t) % 4];
//                    var other = personId == 1 && t == 0 ? "Minister of Finance (2025–present)" : null;
//                    rows.Add(new MpRow(Card(personId, "Member of Parliament", party, TermLabels[t]), TermLabels[t],
//                        RegionName(c.RegionId), c.Name, party, other));
//                }
//            }

//            IEnumerable<MpRow> q = rows;
//            if (filter.Term is { Length: > 0 }) q = q.Where(r => r.Term == filter.Term);
//            if (filter.RegionId is not null) q = q.Where(r => r.Region == RegionName(filter.RegionId.Value));
//            if (filter.ConstituencyId is not null)
//            {
//                var name = AllConstituencies.FirstOrDefault(c => c.Id == filter.ConstituencyId)?.Name;
//                q = q.Where(r => r.Constituency == name);
//            }
//            if (filter.PartyId == 0) q = q.Where(r => r.PartyDuringTerm is null);          // independent / no party
//            else if (filter.PartyId is not null) q = q.Where(r => r.PartyDuringTerm?.Id == filter.PartyId);
//            if (filter.Name is { Length: > 0 }) q = q.Where(r => r.Person.FullName.Contains(filter.Name, StringComparison.OrdinalIgnoreCase));

//            var (page, info) = Page(q, filter.Page, 12);
//            var constituencies = filter.RegionId is null ? new List<Option>() : GetConstituencies(filter.RegionId.Value).ToList();

//            return new ParliamentPageModel(
//                filter,
//                TermLabels.Select(t => new Option(t, $"Parliamentary term {t}")).ToList(),
//                RegionNames.Select((n, i) => new Option((i + 1).ToString(), n)).ToList(),
//                constituencies,
//                Parties.Select(p => new Option(p.Id.ToString(), $"{p.Name} ({p.Abbreviation})"))
//                       .Append(new Option("0", "Independent / no party")).ToList(),
//                page, info);
//        }

//        // ---- Ministries and offices

//        public MinistryPageModel GetMinistry(int? ministryId, int? administrationId)
//        {
//            var mId = ministryId is >= 1 and <= 6 ? ministryId.Value : 1;
//            var aIdx = administrationId is >= 1 and <= 4 ? administrationId.Value - 1 : 0;
//            var mIdx = mId - 1;
//            var admin = Admins[aIdx];

//            var previous = Enumerable.Range(0, 4).Where(a => a != aIdx)
//                .Select(a => Holder(mIdx, a, 0, $"Minister ({Admins[a].Name})")).ToList();

//            return new MinistryPageModel(
//                MinistryList.Select(m => new Option(m.Id.ToString(), MinistryNameFor(m.Id, AdminStarts[0]))).ToList(),
//                Admins.Select(a => new Option(a.Id.ToString(), a.Name + (a.IsCurrent ? " (current)" : ""))).ToList(),
//                mId, admin.Id,
//                MinistryNameFor(mId, admin.Start),
//                MinistryList[mIdx].Description,
//                "https://example.org/ministry-website",
//                Holder(mIdx, aIdx, 0, "Minister"),
//                new[] { Holder(mIdx, aIdx, 1, "Deputy Minister"), Holder(mIdx, aIdx, 2, "Deputy Minister") },
//                new[] { Holder(mIdx, aIdx, 3, "Director-General (sample role)") },
//                NameEras(mId),
//                previous);
//        }

//        public OfficesPageModel GetOffices(int? administrationId)
//        {
//            var aIdx = administrationId is >= 1 and <= 4 ? administrationId.Value - 1 : 0;
//            return new OfficesPageModel(
//                Admins.Select(a => new Option(a.Id.ToString(), a.Name + (a.IsCurrent ? " (current)" : ""))).ToList(),
//                Admins[aIdx].Id,
//                OfficeList.Select((o, i) => new PublicOfficeModel(o.Id, o.Title, o.Description, OfficeCard(i, aIdx), Admins[aIdx].Name)).ToList());
//        }

//        // ---- Archive

//        static IReadOnlyList<SeatRow> Seats(int aIdx) => aIdx switch
//        {
//            0 => new SeatRow[] { new("CPP", 2), new("Independent", 3), new("NDC", 150), new("NPP", 120) },
//            1 => new SeatRow[] { new("CPP", 0), new("Independent", 4), new("NDC", 140), new("NPP", 131) },
//            2 => new SeatRow[] { new("CPP", 0), new("Independent", 1), new("NDC", 148), new("NPP", 126) },
//            _ => new SeatRow[] { new("CPP", 1), new("Independent", 2), new("NDC", 116), new("NPP", 156) }
//        };

//        public ArchivePageModel GetArchive(int? administrationId, int? compareWithId)
//        {
//            var aIdx = administrationId is >= 1 and <= 4 ? administrationId.Value - 1 : 0;
//            int? cIdx = compareWithId is >= 1 and <= 4 && compareWithId != administrationId ? compareWithId.Value - 1 : null;
//            var a = Admins[aIdx];

//            var timeline = new List<TimelineEvent>
//        {
//            new(new DateOnly(a.ElectionYear, 12, 7), $"{a.ElectionYear} general election held", "Election"),
//            new(a.Start, $"Administration begins · President {a.President}", "Administration"),
//            new(a.Start.AddDays(1), $"Vice President {a.VicePresident} takes office", "Appointment"),
//            new(a.Start.AddDays(25), $"Minister of Finance appointed · {Holder(0, aIdx, 0, "").Person.FullName}", "Appointment"),
//            new(a.Start.AddDays(25), $"Minister of Health appointed · {Holder(1, aIdx, 0, "").Person.FullName}", "Appointment")
//        };
//            if (aIdx == 0)
//                timeline.Add(new(a.Start, "Ministry of Communication and Digital Technology adopts its current name", "Ministry change"));
//            if (a.End is not null)
//                timeline.Add(new(a.End.Value, "Administration ends; successor administration begins the next day", "Administration"));

//            CompareRow Row(string label, Func<int, string> f) => new(label, f(aIdx), cIdx is null ? null : f(cIdx.Value));
//            var compareRows = new List<CompareRow>
//        {
//            Row("President", i => Admins[i].President),
//            Row("Vice President", i => Admins[i].VicePresident),
//            Row("Election cycle", i => Admins[i].ElectionYear.ToString()),
//            Row("Period", i => $"{Admins[i].Start:d MMM yyyy} – {(Admins[i].End is null ? "present" : Admins[i].End!.Value.ToString("d MMM yyyy"))}"),
//            Row("Minister of Finance", i => Holder(0, i, 0, "").Person.FullName),
//            Row("Minister of Health", i => Holder(1, i, 0, "").Person.FullName),
//            Row("Minister of Defence", i => Holder(2, i, 0, "").Person.FullName)
//        };

//            return new ArchivePageModel(
//                Admins, a, cIdx is null ? null : Admins[cIdx.Value],
//                timeline.OrderBy(t => t.On).ToList(),
//                Seats(aIdx), cIdx is null ? null : Seats(cIdx.Value), compareRows);
//        }

//        // ---- Search

//        public SearchPageModel Search(string? query)
//        {
//            var q = (query ?? "").Trim();
//            var items = new List<SearchResultItem>();
//            for (var id = 1; id <= PeopleCount; id++)
//                items.Add(new(Name(id), "Person", PartyOf(id)?.Abbreviation ?? "Independent", $"/people/{id}"));
//            items.AddRange(Parties.Select(p => new SearchResultItem($"{p.Name} ({p.Abbreviation})", "Party", null, $"/parties/{p.Id}")));
//            items.AddRange(MinistryList.Select(m => new SearchResultItem(MinistryNameFor(m.Id, AdminStarts[0]), "Ministry", null, $"/ministries?ministryId={m.Id}")));
//            items.AddRange(RegionNames.Select((n, i) => new SearchResultItem($"{n} Region", "Region", null, $"/elections/parliamentary?regionId={i + 1}")));
//            items.AddRange(AllConstituencies.Select(c => new SearchResultItem(c.Name, "Constituency", $"{RegionName(c.RegionId)} Region",
//                $"/elections/parliamentary?regionId={c.RegionId}&constituencyId={c.Id}")));
//            items.AddRange(new[] { "Member of Parliament", "Minister", "Deputy Minister", "President", "Vice President", "Attorney-General", "Chief of Staff", "Speaker of Parliament" }
//                .Select(p => new SearchResultItem(p, "Position", null, "/parliament")));
//            items.AddRange(Cycles.Select(c => new SearchResultItem(c.Name, "Election year", null, $"/elections/presidential?year={c.Year}")));
//            items.AddRange(Admins.Select(a => new SearchResultItem(a.Name, "Administration", null, $"/archive?administrationId={a.Id}")));

//            var hits = q.Length == 0
//                ? new List<SearchResultItem>()
//                : items.Where(i => i.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
//                                || (i.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
//                                || i.Type.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
//            return new SearchPageModel(q, hits);
//        }

//        public StaticPageModel? GetStaticPage(string slug) => slug switch
//        {
//            "about" => new StaticPageModel("About this platform",
//                "Ghana Political Directory is an independent informational platform. It documents political leadership, parties, elections, ministries and administrations. It does not conduct elections, register voters or accept votes.",
//                new[]
//                {
//                ("What it is", "A neutral, historical and searchable record. Presentation never ranks or endorses a party or candidate."),
//                ("What it is not", "It is not a government service, a campaign website or an official source of election results. Official results are those published by the Electoral Commission of Ghana."),
//                ("Independence", "This platform is independent unless formal government authorisation is stated here.")
//                }),
//            "contact" => new StaticPageModel("Contact",
//                "Questions about the platform or its content.",
//                new[] { ("Email", "[contact address to be configured]"), ("Corrections", "To report an error, use the Corrections page so it can be reviewed and logged.") }),
//            "data-sources" => new StaticPageModel("Data sources",
//                "Every important public fact should link to a reliable source.",
//                new[]
//                {
//                ("Priority sources", "Electoral Commission publications, Parliament of Ghana records, official government announcements and ministry publications."),
//                ("What we record", "Source title, URL, publication date, verification status and the date the information was last checked."),
//                ("Result states", "Provisional, verified, officially declared, corrected and published. Results are labelled official only after verification against the relevant authority.")
//                }),
//            "privacy" => new StaticPageModel("Privacy policy",
//                "Placeholder for the privacy policy.",
//                new[] { ("Status", "[to be written and legally reviewed before launch]") }),
//            "terms" => new StaticPageModel("Terms of use",
//                "Placeholder for the terms of use.",
//                new[] { ("Status", "[to be written and legally reviewed before launch]") }),
//            "corrections" => new StaticPageModel("Corrections",
//                "We correct errors and keep a record of important changes.",
//                new[]
//                {
//                ("How to report an error", "Tell us which page, which fact, and (if possible) the official source that shows the correct information."),
//                ("What happens next", "A reviewer checks the source. Corrected election results and appointments keep a change history and appear in the audit log.")
//                }),
//            _ => null
//        };

//        // ------------------------------------------------------------------ admin

//        public AdminDashboardModel GetAdminDashboard() => new(
//            new[]
//            {
//            new SummaryCard("Total people", PeopleCount, "bi-people", "People records (one per person)", "/admin/people"),
//            new SummaryCard("Political parties", Parties.Length, "bi-flag", null, null),
//            new SummaryCard("Election cycles", Cycles.Length, "bi-calendar-event", "Including upcoming", null),
//            new SummaryCard("Constituencies", AllConstituencies.Count, "bi-geo-alt", "Sample subset", null),
//            new SummaryCard("Ministries", MinistryList.Length, "bi-briefcase", null, null),
//            new SummaryCard("Public offices", OfficeList.Length, "bi-building", "Not tied to a ministry", null),
//            new SummaryCard("Published results", 7, "bi-check2-circle", "Verified and published", "/admin/results"),
//            new SummaryCard("Awaiting verification", 5, "bi-hourglass-split", "Needs a second reviewer", "/admin/results")
//            },
//            new[]
//            {
//            new AwaitingItem("2024 Parliamentary · Ablekuma Central", "Election result", "Data entry (A. Sample)", new DateOnly(2026, 10, 9), "Draft"),
//            new AwaitingItem("2024 Parliamentary · Tamale Central", "Election result", "Data entry (A. Sample)", new DateOnly(2026, 10, 9), "Draft"),
//            new AwaitingItem("Minister of Health appointment", "Appointment", "Data entry (B. Sample)", new DateOnly(2026, 10, 8), "Awaiting verification"),
//            new AwaitingItem("Bulk import: 2020 constituency results", "Import", "Data entry (A. Sample)", new DateOnly(2026, 10, 7), "Needs review"),
//            new AwaitingItem("Party affiliation: Kofi Adjei Darko", "Affiliation", "Data entry (B. Sample)", new DateOnly(2026, 10, 6), "Awaiting verification")
//            },
//            new[]
//            {
//            new AuditEntry(new DateTime(2026, 10, 12, 16, 42, 0), "verifier.sample", "Marked verified", "2024 Presidential result"),
//            new AuditEntry(new DateTime(2026, 10, 12, 11, 5, 0), "approver.sample", "Published", "2024 Presidential result"),
//            new AuditEntry(new DateTime(2026, 10, 11, 9, 30, 0), "admin.sample", "Closed appointment record", "Minister of Health (previous administration)"),
//            new AuditEntry(new DateTime(2026, 10, 9, 14, 12, 0), "dataentry.sample", "Created draft", "2024 Parliamentary · Ablekuma Central"),
//            new AuditEntry(new DateTime(2026, 10, 8, 10, 3, 0), "admin.sample", "Added position to existing person", "Kofi Adjei Darko"),
//            new AuditEntry(new DateTime(2026, 10, 7, 15, 50, 0), "admin.sample", "Renamed ministry (history preserved)", "Ministry of Communication and Digital Technology")
//            });

//        public AdminPeopleModel GetAdminPeople(string? q, string? status, int page)
//        {
//            var statuses = new[] { "Published", "Published", "Draft", "Published", "Awaiting verification", "Archived" };
//            var rows = Enumerable.Range(1, PeopleCount).Select(id =>
//            {
//                var positions = id switch
//                {
//                    1 => "MP, Minister",
//                    2 => "President",
//                    3 => "Vice President",
//                    _ when id % 4 == 0 => "Minister",
//                    _ when id % 3 == 0 => "MP",
//                    _ => "Candidate"
//                };
//                return new AdminPersonRow(id, Name(id), positions, PartyOf(id)?.Abbreviation ?? "Independent",
//                    statuses[id % statuses.Length], new DateOnly(2026, 10, 1).AddDays(-(id % 20)));
//            });

//            if (!string.IsNullOrWhiteSpace(q))
//                rows = rows.Where(r => r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
//                                    || r.Positions.Contains(q, StringComparison.OrdinalIgnoreCase)
//                                    || r.Party.Contains(q, StringComparison.OrdinalIgnoreCase));
//            if (!string.IsNullOrWhiteSpace(status))
//                rows = rows.Where(r => r.Status == status);

//            var (items, info) = Page(rows, page, 8);
//            return new AdminPeopleModel(q, status, items, info);
//        }

//        public IReadOnlyList<SimilarPerson> FindSimilarPeople(string name)
//        {
//            var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3).ToArray();
//            if (tokens.Length == 0) return Array.Empty<SimilarPerson>();
//            return Enumerable.Range(1, PeopleCount)
//                .Where(id => tokens.Any(t => Name(id).Contains(t, StringComparison.OrdinalIgnoreCase)))
//                .Take(5)
//                .Select(id => new SimilarPerson(id, Name(id), $"{PartyOf(id)?.Abbreviation ?? "Independent"} · profile #{id} (sample)"))
//                .ToList();
//        }

//        public AdminFormOptions GetFormOptions() => new(
//            Parties.Select(p => new Option(p.Id.ToString(), $"{p.Name} ({p.Abbreviation})")).ToList(),
//            GetElectionCycleOptions(),
//            Admins.Select(a => new Option(a.Id.ToString(), a.Name)).ToList(),
//            MinistryList.Select(m => new Option(m.Id.ToString(), m.Name)).ToList(),
//            RegionNames.Select((n, i) => new Option((i + 1).ToString(), n)).ToList(),
//            new[] { "Active", "Acting", "Ended", "Draft (unconfirmed)" }.Select(s => new Option(s, s)).ToList());

//        public IReadOnlyList<Option> GetElectionCycleOptions() =>
//            Cycles.Select(c => new Option(c.Id.ToString(), c.Name)).ToList();

//        public IReadOnlyList<ImportIssue> GetSampleImportReport() => new[]
//        {
//        new ImportIssue(3, "candidate", "Candidate not found. Select an existing candidate or create one.", "Error"),
//        new ImportIssue(7, "votes", "Vote total is not a whole number.", "Error"),
//        new ImportIssue(9, "candidate + constituency", "Duplicate of row 8.", "Error"),
//        new ImportIssue(12, "total_votes_cast", "Valid votes plus rejected ballots do not equal total votes cast.", "Warning"),
//        new ImportIssue(15, "party", "Party abbreviation \"NDP\" does not match a known party.", "Error")
//    };
//    }
//}
