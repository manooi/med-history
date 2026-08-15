using System.Diagnostics;
using MedHistory.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

[AllowAnonymous]
public class ErrorController : Controller
{
    // /Home/Error is kept so an older UseExceptionHandler path still resolves.
    [HttpGet("/error")]
    [HttpGet("/Home/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        return View("Error", new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
