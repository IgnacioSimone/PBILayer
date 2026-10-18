using System.Globalization;
using System.Reflection;

namespace PBIExplorer.Services;

/// <summary>Buscador de texto genérico por reflexión: cualquier propiedad string del
/// objeto cuenta para el filtro, así no hay que mantener una lista de campos
/// "buscables" por tipo a mano.</summary>
public static class SearchMatcher
{
    private static readonly Dictionary<Type, PropertyInfo[]> Cache = new();
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

    public static bool Matches(object item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;

        var type = item.GetType();
        PropertyInfo[] props;
        lock (Cache)
        {
            if (!Cache.TryGetValue(type, out props!))
            {
                props = type.GetProperties()
                    .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                    .ToArray();
                Cache[type] = props;
            }
        }

        foreach (var p in props)
        {
            // Sin distinguir mayúsculas ni acentos: "pagina" encuentra "Página".
            if (p.GetValue(item) is string value &&
                Comparer.IndexOf(value, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                return true;
        }
        return false;
    }
}
