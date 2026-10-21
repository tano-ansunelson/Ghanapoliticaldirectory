using Ghanapoliticaldirectory.Models;
using Microsoft.AspNetCore.Mvc;

using Ghanapoliticaldirectory.Services;
using System.Diagnostics;

namespace Ghanapoliticaldirectory.Controllers;

public class HomeController : Controller
{
    private readonly IDirectoryReader _data;
    public HomeController(IDirectoryReader data) => _data = data;

    public IActionResult Index() => View(_data.GetHome());

    [Route("error")]
    public IActionResult Error()
    {
        return View();
    }
   
}
