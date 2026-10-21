//using Ghanapoliticaldirectory.Models;
//using Ghanapoliticaldirectory.Service;
//using Microsoft.AspNetCore.Mvc;

using Ghanapoliticaldirectory.Models;
using Ghanapoliticaldirectory.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ghanapoliticaldirectory.Controllers;

[Route("people")]
public class PeopleController : Controller
{
    private readonly IDirectoryReader _data;
    public PeopleController(IDirectoryReader data) => _data = data;

    [HttpGet("{id:int}")]
    public IActionResult Details(int id)
    {
        var person = _data.GetPerson(id);
        return person is null ? NotFound() : View(person);
    }
}


[Route("presidency")]
public class PresidencyController : Controller
{
    private readonly IDirectoryReader _data;
    public PresidencyController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index() => View(_data.GetPresidency());
}

[Route("parties")]
public class PartiesController : Controller
{
    private readonly IDirectoryReader _data;
    public PartiesController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index() => View(_data.GetParties());

    // Filtering is server-side (GET query string) so every filtered view is linkable.
    [HttpGet("{id:int}")]
    public IActionResult Details(int id, int? year, int? administrationId, string? position, int? regionId, int page = 1)
    {
        var model = _data.GetParty(id, new PartyFilter(year, administrationId, position, regionId, page));
        return model is null ? NotFound() : View(model);
    }
}



[Route("elections")]
public class ElectionsController : Controller
{
    private readonly IDirectoryReader _data;
    public ElectionsController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Presidential));

    [HttpGet("presidential")]
    public IActionResult Presidential(int? year) => View(_data.GetPresidential(year));

    [HttpGet("parliamentary")]
    public IActionResult Parliamentary(int? year, int? regionId, int? constituencyId) =>
        View(_data.GetParliamentary(year, regionId, constituencyId));

    /// <summary>Feeds the dependent Region → Constituency drop-down (progressive enhancement).</summary>
    [HttpGet("constituencies")]
    public IActionResult Constituencies(int regionId) => Json(_data.GetConstituencies(regionId));
}

[Route("parliament")]
public class ParliamentController : Controller
{
    private readonly IDirectoryReader _data;
    public ParliamentController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index(string? term, int? regionId, int? constituencyId, int? partyId, string? name, int page = 1) =>
        View(_data.GetParliament(new ParliamentFilter(term, regionId, constituencyId, partyId, name, page)));
}



[Route("ministries")]
public class MinistriesController : Controller
{
    private readonly IDirectoryReader _data;
    public MinistriesController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index(int? ministryId, int? administrationId) =>
        View(_data.GetMinistry(ministryId, administrationId));

    [HttpGet("offices")]
    public IActionResult Offices(int? administrationId) => View(_data.GetOffices(administrationId));
}


[Route("archive")]
public class ArchiveController : Controller
{
    private readonly IDirectoryReader _data;
    public ArchiveController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index(int? administrationId, int? compareWith) =>
        View(_data.GetArchive(administrationId, compareWith));
}

[Route("search")]
public class SearchController : Controller
{
    private readonly IDirectoryReader _data;
    public SearchController(IDirectoryReader data) => _data = data;

    [HttpGet("")]
    public IActionResult Index(string? q) => View(_data.Search(q));

    /// <summary>JSON for the type-ahead dropdown. Capped so the response stays small.</summary>
    [HttpGet("suggest")]
    public IActionResult Suggest(string? q) => Json(_data.Search(q).Items.Take(8));
}


public class PagesController : Controller
{
    private readonly IDirectoryReader _data;
    public PagesController(IDirectoryReader data) => _data = data;

    [HttpGet("/about")] public IActionResult About() => Render("about");
    [HttpGet("/contact")] public IActionResult Contact() => Render("contact");
    [HttpGet("/data-sources")] public IActionResult DataSources() => Render("data-sources");
    [HttpGet("/privacy")] public IActionResult Privacy() => Render("privacy");
    [HttpGet("/terms")] public IActionResult Terms() => Render("terms");
    [HttpGet("/corrections")] public IActionResult Corrections() => Render("corrections");

    private IActionResult Render(string slug)
    {
        var page = _data.GetStaticPage(slug);
        return page is null ? NotFound() : View("Show", page);
    }
}

