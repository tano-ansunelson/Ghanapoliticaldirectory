using Ghanapoliticaldirectory.Models;

namespace Ghanapoliticaldirectory.Services;

/// <summary>
/// The only data contract the public views know about. EfDirectoryReader implements it over the database. Filters must use recorded dates and relationships (historical), never a
/// person's "current" status.
/// </summary>
public interface IDirectoryReader
{
    // Public site
    HomeViewModel GetHome();
    PersonProfileModel? GetPerson(int id);
    IReadOnlyList<PresidencyRow> GetPresidency();
    IReadOnlyList<PartyRef> GetParties();
    PartyPageModel? GetParty(int partyId, PartyFilter filter);
    PresidentialPageModel GetPresidential(int? year);
    ParliamentaryPageModel GetParliamentary(int? year, int? regionId, int? constituencyId);
    IReadOnlyList<Option> GetConstituencies(int regionId);
    ParliamentPageModel GetParliament(ParliamentFilter filter);
    MinistryPageModel GetMinistry(int? ministryId, int? administrationId);
    OfficesPageModel GetOffices(int? administrationId);
    ArchivePageModel GetArchive(int? administrationId, int? compareWithId);
    SearchPageModel Search(string? query);
    StaticPageModel? GetStaticPage(string slug);

    /// <summary>True when any published record is development sample data, so the site can say so.</summary>
    bool HasSampleData();

    // Administration
    AdminDashboardModel GetAdminDashboard();
    AdminPeopleModel GetAdminPeople(string? q, string? status, int page);
    IReadOnlyList<SimilarPerson> FindSimilarPeople(string name);
    AdminFormOptions GetFormOptions();
    IReadOnlyList<Option> GetElectionCycleOptions();
}
