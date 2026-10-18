using PBIExplorer.Infrastructure;

namespace PBIExplorer.Services;

/// <summary>
/// Los términos del modelo de Power BI ("Medida", "Columna", "Ambos sentidos", "Muchos a muchos"…) se guardan siempre
/// en español, que es su valor canónico: así un snapshot .json se puede comparar con otro aunque la app estuviera en
/// otro idioma, y la lógica (uso, rendimiento) compara contra un único texto. Lo que se traduce es lo que se MUESTRA:
/// pantalla y archivos exportados pasan por <see cref="Display"/>.
/// </summary>
public static class Terms
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["Medida"] = "Term_Measure",
        ["Columna"] = "Term_Column",
        ["Tabla"] = "Term_Table",
        ["Consulta"] = "Term_Query",
        ["Jerarquía"] = "Term_Hierarchy",
        ["Reporte"] = "Term_Report",
        ["Página"] = "Term_Page",
        ["Visual"] = "Term_Visual",

        ["Automático"] = "Term_TypeAuto",
        ["Texto"] = "Term_TypeText",
        ["Número entero"] = "Term_TypeInteger",
        ["Número decimal"] = "Term_TypeDecimal",
        ["Fecha/Hora"] = "Term_TypeDateTime",
        ["Decimal fijo"] = "Term_TypeFixedDecimal",
        ["Booleano"] = "Term_TypeBoolean",
        ["Binario"] = "Term_TypeBinary",
        ["Desconocido"] = "Term_TypeUnknown",
        ["Variante"] = "Term_TypeVariant",

        ["De datos"] = "Term_ColData",
        ["Calculada"] = "Term_ColCalculated",
        ["RowNumber (interna)"] = "Term_ColRowNumber",
        ["De tabla calculada"] = "Term_ColFromCalcTable",

        ["Único sentido"] = "Term_FilterOneWay",
        ["Ambos sentidos"] = "Term_FilterBoth",

        ["Uno a muchos"] = "Term_CardOneToMany",
        ["Muchos a uno"] = "Term_CardManyToOne",
        ["Muchos a muchos"] = "Term_CardManyToMany",
        ["Uno a uno"] = "Term_CardOneToOne",

        ["Parámetro / función"] = "Term_QueryParamFunction",
    };

    /// <summary>El término en el idioma de la interfaz. Lo que no se conoce (un tipo nuevo de Power BI) se muestra tal cual.</summary>
    public static string Display(string? canonical)
    {
        if (string.IsNullOrEmpty(canonical)) return "";
        if (Loc.Code == "es") return canonical;
        return Keys.TryGetValue(canonical, out var key) ? Loc.T(key) : canonical;
    }

    /// <summary>Todos los valores canónicos conocidos (para las pruebas de que cada uno tiene traducción).</summary>
    public static IReadOnlyCollection<string> Known => Keys.Keys;
}
