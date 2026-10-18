using System.Globalization;
using System.Runtime.CompilerServices;
using PBIExplorer.Infrastructure;

namespace PBIExplorer.Tests;

/// <summary>Los tests comparan textos en español: se fija el idioma sin depender del de Windows.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Init() => Loc.SetCulture(new CultureInfo("es-AR"));
}
