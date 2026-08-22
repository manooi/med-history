using MedHistory.Data;
using MedHistory.Models;
using MedHistory.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace MedHistory.Controllers;

/// <summary>
/// Manages the entry types the day view offers. Adding a type here is what replaces
/// editing an enum and shipping a build; types are retired by deactivating them so the
/// entries already logged under them keep their name.
/// </summary>
public class TypesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<TypesController> _logger;

    // EntryTypeRules hands back keys; this is what turns them into the reader's copy before
    // they reach ModelState, which asp-validation-summary renders as it finds them.
    private readonly IStringLocalizer<SharedResource> _localizer;

    public TypesController(
        AppDbContext db,
        ILogger<TypesController> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    [HttpGet("/types")]
    public async Task<IActionResult> Index() => View(await BuildModel());

    [HttpPost("/types")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? name)
    {
        var existingNames = await _db.EntryTypes.AsNoTracking().Select(t => t.Name).ToListAsync();
        var errors = EntryTypeRules.ValidateNewName(name, existingNames);

        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, _localizer.Localize(error));
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModel(name));
        }

        // Non-null: ValidateNewName rejects a name that normalises away.
        var normalized = EntryTypeRules.NormalizeName(name)!;

        _db.EntryTypes.Add(new EntryTypeDef
        {
            Name = normalized,
            IsActive = true,
            IsBuiltIn = false
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index on lower(Name) is the real guard; the check above only
            // beats it if two adds race, which is worth a readable message rather than a 500.
            _db.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty,
                _localizer["A type named \"{0}\" already exists.", normalized]);
            return View("Index", await BuildModel(name));
        }

        _logger.LogInformation("Entry type {TypeName} added", normalized);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/types/{id:int}/activate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(int id) => SetActive(id, true);

    [HttpPost("/types/{id:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(int id) => SetActive(id, false);

    private async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var type = await _db.EntryTypes.FindAsync(id);

        if (type is null)
        {
            return NotFound();
        }

        type.IsActive = isActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Entry type {TypeName} active set to {IsActive}", type.Name, isActive);

        return RedirectToAction(nameof(Index));
    }

    private async Task<TypesViewModel> BuildModel(string? newName = null)
    {
        var types = await _db.EntryTypes
            .AsNoTracking()
            .Select(t => new EntryTypeRow
            {
                Id = t.Id,
                Name = t.Name,
                IsActive = t.IsActive,
                IsBuiltIn = t.IsBuiltIn
            })
            .ToListAsync();

        return new TypesViewModel
        {
            Types = EntryTypeRules.SortForDisplay(types, t => t.Name),
            NewName = newName
        };
    }
}
