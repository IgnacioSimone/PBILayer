using System.Reflection;

namespace PBIExplorer.Services;

/// <summary>Buscador de texto genérico por reflexión: cualquier propiedad string del
/// objeto cuenta para el filtro, así no hay que mantener una lista de campos
/// "buscables" por tipo a mano.</summary>
public static class SearchMatcher
{
    private static readonly Dictionary<Type, PropertyInfo[]> Cache = new();

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
            if (p.GetValue(item) is string value && value.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
