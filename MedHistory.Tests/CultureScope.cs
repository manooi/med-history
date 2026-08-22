using System.Globalization;

namespace MedHistory.Tests;

/// <summary>
/// Sets the ambient culture for the length of a block and puts back exactly what was there.
///
/// The identity helpers are what this exists for: proving <see cref="MedHistory.Services.AppTime.Key"/>
/// and friends still write a Gregorian ISO string while the thread's own culture would render a
/// Buddhist-era year for anything formatted implicitly. Restoring matters as much as setting —
/// the ambient culture is thread-state that xUnit's runner reuses across tests, so a scope left
/// open would decide the outcome of whatever ran next on that thread.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    public CultureScope(string name)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;

        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
