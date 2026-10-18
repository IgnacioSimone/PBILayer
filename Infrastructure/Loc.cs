using System.Globalization;
using System.Resources;

namespace PBIExplorer.Infrastructure;

/// <summary>Idioma de la interfaz: seguir el de Windows, o fijar español o inglés.</summary>
public enum AppLanguage
{
    System,
    Spanish,
    English
}

/// <summary>
/// Textos de la aplicación en el idioma elegido. Los textos viven en Resources\Strings.resx (español, el idioma
/// base) y Resources\Strings.en.resx (inglés); este es el único lugar que los lee.
/// El idioma se fija una vez al arrancar: cambiarlo pide reiniciar la app (como casi todas las aplicaciones de escritorio).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("PBIExplorer.Resources.Strings", typeof(Loc).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("es");

    /// <summary>"es" o "en": el idioma que se está usando.</summary>
    public static string Code => Culture.TwoLetterISOLanguageName;

    /// <summary>Resuelve la elección del usuario. En automático: español si Windows está en español, inglés en cualquier otro caso.</summary>
    public static CultureInfo Resolve(AppLanguage language, CultureInfo? system = null)
    {
        system ??= CultureInfo.CurrentUICulture;
        return language switch
        {
            AppLanguage.Spanish => CultureInfo.GetCultureInfo("es"),
            AppLanguage.English => CultureInfo.GetCultureInfo("en"),
            _ => CultureInfo.GetCultureInfo(system.TwoLetterISOLanguageName == "es" ? "es" : "en")
        };
    }

    public static void Initialize(AppLanguage language) => SetCulture(Resolve(language));

    public static void SetCulture(CultureInfo culture)
    {
        Culture = culture;

        // Solo el idioma de los textos: los formatos de número y fecha siguen siendo los de Windows.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>El texto de la clave en el idioma actual. Si falta, devuelve la clave (se nota enseguida y no rompe nada).</summary>
    public static string T(string key)
    {
        try
        {
            return Resources.GetString(key, Culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    /// <summary>Igual que <see cref="T"/> pero con valores en {0}, {1}…</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);
}
