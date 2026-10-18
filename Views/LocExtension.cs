using System.Windows.Markup;
using PBIExplorer.Infrastructure;

namespace PBIExplorer.Views;

/// <summary>Texto traducido en XAML: <c>Content="{views:Loc Btn_Export}"</c>. Con <c>Upper=True</c> sale en mayúsculas
/// (encabezados de columna).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public bool Upper { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var text = Loc.T(Key);
        return Upper ? text.ToUpper(Loc.Culture) : text;
    }
}
