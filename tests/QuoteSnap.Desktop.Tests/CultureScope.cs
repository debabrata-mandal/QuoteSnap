using System.Globalization;

namespace QuoteSnap.Desktop.Tests;

/// <summary>Sets the current culture until disposed.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public CultureScope(string name) => CultureInfo.CurrentCulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
