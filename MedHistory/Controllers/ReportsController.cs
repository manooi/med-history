using Microsoft.AspNetCore.Mvc;

namespace MedHistory.Controllers;

/// <summary>
/// The hub page linking the individual reports — med adherence, anxiety, and per-type entry
/// log. Nothing here reads or writes anything; it exists only because those three lived with no
/// single place that named all of them together.
/// </summary>
public class ReportsController : Controller
{
    [HttpGet("/report")]
    public IActionResult Index() => View();
}
