using CommunityToolkit.Mvvm.Input;
using PBIExplorer.Models;
using PBIExplorer.Services;

namespace PBIExplorer.ViewModels;

public sealed record DetailFact(string Label, string Value);

public static class DetailFacts
{
    /// <summary>Arma la lista de datos descartando los que vienen vacíos.</summary>
    public static IReadOnlyList<DetailFact> Of(params (string Label, string? Value)[] facts) =>
        facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => new DetailFact(f.Label, f.Value!)).ToList();
}

/// <summary>Una dependencia que se puede abrir: al hacer clic se salta a ese objeto.</summary>
public sealed class DependencyLink
{
    public DependencyLink(string label, Action<string> navigate)
    {
        Label = label;
        OpenCommand = new RelayCommand(() => navigate(label));
    }

    public string Label { get; }
    public IRelayCommand OpenCommand { get; }
}

/// <summary>Qué le hace falta al panel de detalle de afuera: dependencias, saltar a otro objeto, copiar.</summary>
public sealed class DetailContext
{
    public Func<string, string, string, ObjectUsage?> FindUsage { get; init; } = (_, _, _) => null;
    public Action<string> Navigate { get; init; } = _ => { };
    public Action<string> Copy { get; init; } = _ => { };
}

/// <summary>Contenido del panel lateral de una medida, columna o consulta: el código completo,
/// sus datos y de qué depende / quién la usa.</summary>
public sealed class DetailViewModel
{
    public DetailViewModel(string title, string subtitle, string? code, CodeLanguage language,
        IReadOnlyList<DetailFact> facts, ObjectUsage? usage, DetailContext context)
    {
        Title = title;
        Subtitle = subtitle;
        Code = code;
        Language = language;
        Facts = facts;
        StatusText = usage?.Status;
        DependsOn = (usage?.DependsOn ?? new List<string>()).Select(l => new DependencyLink(l, context.Navigate)).ToList();
        UsedBy = (usage?.UsedBy ?? new List<string>()).Select(l => new DependencyLink(l, context.Navigate)).ToList();
        CopyCommand = new RelayCommand(() => { if (!string.IsNullOrEmpty(Code)) context.Copy(Code); }, () => !string.IsNullOrEmpty(Code));
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string? Code { get; }
    public CodeLanguage Language { get; }
    public IReadOnlyList<DetailFact> Facts { get; }

    /// <summary>"En uso", "Solo uso interno" o "Sin uso — candidata a eliminar" (null si no hay análisis de uso).</summary>
    public string? StatusText { get; }

    public IReadOnlyList<DependencyLink> DependsOn { get; }
    public IReadOnlyList<DependencyLink> UsedBy { get; }
    public IRelayCommand CopyCommand { get; }

    public bool HasCode => !string.IsNullOrEmpty(Code);
    public bool HasDependsOn => DependsOn.Count > 0;
    public bool HasUsedBy => UsedBy.Count > 0;
}
