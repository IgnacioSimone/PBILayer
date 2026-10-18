using System.Text.Json.Serialization;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services;

namespace PBIExplorer.Models;

/// <summary>Todo lo que se pudo leer del modelo, más las secciones que fallaron.
/// Las colecciones llevan setter (no solo get) aunque el código siempre las mute in
/// place con .Add(...) — System.Text.Json no repuebla una propiedad de colección de
/// solo lectura al deserializar (JsonModelExporter.Load necesita esto para reconstruir
/// un snapshot guardado antes).</summary>
public class ModelSnapshot
{
    public List<TableInfo> Tables { get; set; } = new();
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<MeasureInfo> Measures { get; set; } = new();
    public List<RelationshipInfo> Relationships { get; set; } = new();
    public List<PowerQueryInfo> PowerQueries { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class TableInfo
{
    public string Name { get; set; } = "";
    public bool IsHidden { get; set; }
    public string Description { get; set; } = "";
    public int ColumnCount { get; set; }
    public int MeasureCount { get; set; }
}

public class ColumnInfo
{
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string ColumnType { get; set; } = "";
    public bool IsHidden { get; set; }
    public string SourceColumn { get; set; } = "";
    public string Expression { get; set; } = "";
    public string DisplayFolder { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Una columna calculada muestra su DAX; una normal, la columna física de origen.</summary>
    public string DaxOrSource => string.IsNullOrWhiteSpace(Expression) ? SourceColumn : Expression;

    // DataType y ColumnType se guardan en español (valor canónico); estos son los que se muestran.
    [JsonIgnore] public string DataTypeText => Terms.Display(DataType);
    [JsonIgnore] public string ColumnTypeText => Terms.Display(ColumnType);
}

public class MeasureInfo
{
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string Expression { get; set; } = "";
    public string DisplayFolder { get; set; } = "";
    public string FormatString { get; set; } = "";
    public bool IsHidden { get; set; }
    public string Description { get; set; } = "";
}

public class RelationshipInfo
{
    public string FromTable { get; set; } = "";
    public string FromColumn { get; set; } = "";
    public string ToTable { get; set; } = "";
    public string ToColumn { get; set; } = "";
    public string CrossFilterBehavior { get; set; } = "";
    public bool IsActive { get; set; }
    public string Cardinality { get; set; } = "";

    [JsonIgnore] public string CardinalityText => Terms.Display(Cardinality);
    [JsonIgnore] public string CrossFilterText => Terms.Display(CrossFilterBehavior);
}

public class PowerQueryInfo
{
    /// <summary>Nombre de la tabla o del parámetro/función compartida.</summary>
    public string Name { get; set; } = "";
    /// <summary>"Tabla", "Parámetro" o "Función compartida".</summary>
    public string Kind { get; set; } = "";
    public string MCode { get; set; } = "";
    public bool IsHidden { get; set; }
    public string Description { get; set; } = "";

    [JsonIgnore] public string KindText => Terms.Display(Kind);
}

public class ObjectUsage
{
    public string Kind { get; set; } = "";   // "Medida", "Columna", "Tabla", "Consulta" (parámetro / función / auxiliar de Power Query)
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public bool UsedInVisual { get; set; }
    public bool UsedInRelationship { get; set; }
    public bool UsedInDax { get; set; }      // otra medida/columna calculada depende de este objeto
    public bool UsedInQuery { get; set; }    // otra consulta de Power Query que sí se usa depende de este objeto
    public List<string> DependsOn { get; } = new();  // objetos que ESTE usa en su fórmula
    public List<string> UsedBy { get; } = new();     // objetos que usan a ESTE en su fórmula

    [JsonIgnore]
    public bool IsUnused => !UsedInVisual && !UsedInRelationship && !UsedInDax && !UsedInQuery;

    /// <summary>Veredicto de uso. Es un valor, no un texto, para que las pantallas coloreen por estado sin depender del idioma.</summary>
    [JsonIgnore]
    public UsageState State => IsUnused ? UsageState.Unused
        : !UsedInVisual && (UsedInDax || UsedInRelationship || UsedInQuery) ? UsageState.InternalOnly
        : UsageState.InUse;

    [JsonIgnore]
    public string Status => State switch
    {
        UsageState.Unused => Loc.T("Usage_Unused"),
        UsageState.InternalOnly => Loc.T("Usage_InternalOnly"),
        _ => Loc.T("Usage_InUse")
    };

    [JsonIgnore] public string KindText => Terms.Display(Kind);
    [JsonIgnore] public string DependsOnText => DependsOn.Count > 0 ? string.Join(", ", DependsOn.Select(UsageLabels.Display)) : "";
    [JsonIgnore] public string UsedByText => UsedBy.Count > 0 ? string.Join(", ", UsedBy.Select(UsageLabels.Display)) : "";
}

public enum UsageState
{
    InUse,
    InternalOnly,
    Unused
}

public class VisualFieldUsage
{
    public string Page { get; set; } = "";
    public string VisualType { get; set; } = "";
    public string Title { get; set; } = "";
    public string FieldKind { get; set; } = ""; // Medida / Columna
    public string Table { get; set; } = "";
    public string Field { get; set; } = "";

    [JsonIgnore] public string FieldKindText => Terms.Display(FieldKind);
}

public class FilterInfo
{
    public string Scope { get; set; } = "";        // "Reporte" | "Página" | "Visual"
    public string Page { get; set; } = "";          // vacío para nivel Reporte
    public string VisualTitle { get; set; } = "";   // vacío salvo Scope == "Visual"
    public string VisualType { get; set; } = "";
    public string Name { get; set; } = "";
    public string FieldKind { get; set; } = "";     // Medida / Columna / Jerarquía
    public string Table { get; set; } = "";
    public string Field { get; set; } = "";
    public string FilterType { get; set; } = "";    // Categorical / TopN / Advanced / RelativeDate / ...
    public string Summary { get; set; } = "";       // condición en texto, best-effort

    [JsonIgnore] public string ScopeText => Terms.Display(Scope);
    [JsonIgnore] public string FieldKindText => Terms.Display(FieldKind);

    [JsonIgnore] public string TargetText => $"{Table}[{Field}]";
    [JsonIgnore] public string VisualText => Scope == "Visual" ? (string.IsNullOrEmpty(VisualTitle) ? VisualType : VisualTitle) : "";
}
