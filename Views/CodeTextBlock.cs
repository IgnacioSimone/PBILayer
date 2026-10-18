using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PBIExplorer.Infrastructure;
using PBIExplorer.Services;

namespace PBIExplorer.Views;

/// <summary>TextBlock que muestra código DAX o M con colores de sintaxis. Los colores salen de los
/// recursos Syntax* del tema, y se vuelven a aplicar cuando cambia el tema.</summary>
public sealed class CodeTextBlock : TextBlock
{
    /// <summary>Por encima de este tamaño no se resalta: construir miles de fragmentos congelaría la ventana.</summary>
    private const int MaxHighlightedLength = 60_000;

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(CodeTextBlock), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty SyntaxProperty = DependencyProperty.Register(
        nameof(Syntax), typeof(CodeLanguage), typeof(CodeTextBlock), new PropertyMetadata(CodeLanguage.Dax, OnChanged));

    public CodeTextBlock()
    {
        Loaded += (_, _) =>
        {
            ThemeService.Changed += Rebuild;
            Rebuild();
        };
        Unloaded += (_, _) => ThemeService.Changed -= Rebuild;
    }

    public string? Code
    {
        get => (string?)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>DAX o M. (No se llama "Language" porque FrameworkElement ya tiene una propiedad así, para el idioma del texto.)</summary>
    public CodeLanguage Syntax
    {
        get => (CodeLanguage)GetValue(SyntaxProperty);
        set => SetValue(SyntaxProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodeTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        var code = Code;
        if (string.IsNullOrEmpty(code)) return;

        if (code.Length > MaxHighlightedLength)
        {
            Inlines.Add(new Run(code));
            return;
        }

        foreach (var token in CodeTokenizer.Tokenize(code, Syntax))
        {
            var run = new Run(token.Text);
            if (BrushFor(token.Kind) is { } brush) run.Foreground = brush;
            if (token.Kind == CodeKind.Keyword) run.FontWeight = FontWeights.SemiBold;
            Inlines.Add(run);
        }
    }

    private static Brush? BrushFor(CodeKind kind)
    {
        var key = kind switch
        {
            CodeKind.Keyword => "SyntaxKeyword",
            CodeKind.Function => "SyntaxFunction",
            CodeKind.String => "SyntaxString",
            CodeKind.Number => "SyntaxNumber",
            CodeKind.Comment => "SyntaxComment",
            CodeKind.Reference => "SyntaxReference",
            _ => null
        };
        return key is null ? null : Application.Current?.TryFindResource(key) as Brush;
    }
}
