using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VRCVideoCacher.Analyzers;

/// <summary>
///     Writes the config load/save mappings of a <c>[ConfigBindings(typeof(ConfigModel))]</c> view model,
///     so a settings class does not have to repeat every property by hand. A property binds to a public
///     config field of the same name (case-insensitively) and type; a leading <c>Ytdl</c>/<c>Ytdlp</c>
///     prefix counts as the same prefix. The closest name wins, so <c>YtdlAutoUpdate</c> takes
///     <c>YtdlpAutoUpdate</c> and a bare <c>AutoUpdate</c> does not. Properties that need a special
///     mapping, or are not config values at all, are marked <c>[ConfigBindingIgnore]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ConfigBindingsGenerator : IIncrementalGenerator
{
    private const string Attribute = "VRCVideoCacher.Utils.ConfigBindingsAttribute";
    private const string IgnoreAttribute = "VRCVideoCacher.Utils.ConfigBindingIgnoreAttribute";
    private const string ObservableProperty = "CommunityToolkit.Mvvm.ComponentModel.ObservablePropertyAttribute";
    private const string ChangeTracker = "SetHasChanges";

    private static readonly string[] StripPrefixes = ["ytdlp", "ytdl"];

    private static readonly DiagnosticDescriptor NotPartial = new(
        "VVC3010",
        "Config bindings class must be partial",
        "'{0}' must be a partial class so the generator can add the config bindings",
        "ConfigBindingsGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor Unsupported = new(
        "VVC3011",
        "Config bindings class is not supported",
        "'{0}' must be a top-level, non-generic class with a config model type argument",
        "ConfigBindingsGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor Unbound = new(
        "VVC3012",
        "Observable property has no config binding",
        "'{0}' does not bind this to '{1}': {2}; rename it to match a config field or mark it [ConfigBindingIgnore]",
        "ConfigBindingsGenerator",
        DiagnosticSeverity.Warning,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(
                Attribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => (
                    Type: ctx.TargetSymbol as INamedTypeSymbol,
                    Config: ctx.Attributes.FirstOrDefault()?.ConstructorArguments.FirstOrDefault().Value
                        as INamedTypeSymbol))
            .Where(static item => item.Type is not null);

        context.RegisterSourceOutput(types, static (output, item) => Emit(output, item.Type!, item.Config));
    }

    private static void Emit(SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol? config)
    {
        var location = type.Locations.FirstOrDefault();

        if (config is null || type.ContainingType is not null || type.TypeParameters.Length > 0 ||
            type.TypeKind != TypeKind.Class)
        {
            context.ReportDiagnostic(Diagnostic.Create(Unsupported, location, type.Name));
            return;
        }

        if (!IsPartial(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(NotPartial, location, type.Name));
            return;
        }

        var properties = type.GetMembers().OfType<IPropertySymbol>()
            .Where(property => HasAttribute(property, ObservableProperty) && !HasAttribute(property, IgnoreAttribute))
            .ToArray();

        var fields = config.GetMembers().OfType<IFieldSymbol>()
            .Where(field => field.DeclaredAccessibility == Accessibility.Public && !field.IsStatic && !field.IsConst)
            .ToArray();

        var bindings = Match(properties, fields);

        var bound = new HashSet<ISymbol>(bindings.Select(binding => binding.Property), SymbolEqualityComparer.Default);
        var unbound = properties.Where(property => !bound.Contains(property))
            .Select(property => property.Name)
            .ToArray();
        if (unbound.Length > 0)
            context.ReportDiagnostic(Diagnostic.Create(Unbound, location, type.Name, config.Name,
                string.Join(", ", unbound)));

        var configType = config.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var hasTracker = type.GetMembers(ChangeTracker).OfType<IMethodSymbol>()
            .Any(method => method.Parameters.Length == 0);
        var accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var containingNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } containing
            ? containing.ToDisplayString()
            : null;
        var indent = containingNamespace is null ? "" : "    ";

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();

        if (containingNamespace is not null)
        {
            builder.AppendLine($"namespace {containingNamespace}");
            builder.AppendLine("{");
        }

        builder.AppendLine($"{indent}{accessibility} partial class {type.Name}");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    private void ApplyConfigBindings({configType} config)");
        builder.AppendLine($"{indent}    {{");
        foreach (var binding in bindings)
            builder.AppendLine($"{indent}        {binding.Property.Name} = config.{binding.Field.Name};");
        builder.AppendLine($"{indent}    }}");
        builder.AppendLine();
        builder.AppendLine($"{indent}    private void CaptureConfigBindings({configType} config)");
        builder.AppendLine($"{indent}    {{");
        foreach (var binding in bindings)
            builder.AppendLine($"{indent}        config.{binding.Field.Name} = {binding.Property.Name};");
        builder.AppendLine($"{indent}    }}");

        if (hasTracker)
            foreach (var binding in bindings)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"{indent}    partial void On{binding.Property.Name}Changed({TypeName(binding.Property.Type)} value) => {ChangeTracker}();");
            }

        builder.AppendLine($"{indent}}}");

        if (containingNamespace is not null)
            builder.AppendLine("}");

        context.AddSource($"{type.ToDisplayString().Replace('.', '_')}.ConfigBindings.g.cs",
            SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    /// <summary>
    ///     Greedy match, closest names first: an exact name match always wins over one that only matches
    ///     after dropping the yt-dlp prefix.
    /// </summary>
    private static List<Binding> Match(IReadOnlyList<IPropertySymbol> properties, IReadOnlyList<IFieldSymbol> fields)
    {
        var candidates = new List<(Binding Binding, int Distance)>();
        foreach (var property in properties)
            foreach (var field in fields)
                if (SymbolEqualityComparer.Default.Equals(property.Type, field.Type) &&
                    IsSameName(property.Name, field.Name))
                    candidates.Add((new Binding(property, field), Math.Abs(property.Name.Length - field.Name.Length)));

        var picked = new List<Binding>();
        var takenProperties = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var takenFields = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var (binding, _) in candidates.OrderBy(candidate => candidate.Distance)
                     .ThenBy(candidate => candidate.Binding.Property.Name, StringComparer.Ordinal))
        {
            if (takenProperties.Contains(binding.Property) ||
                takenFields.Contains(binding.Field) ||
                !takenProperties.Add(binding.Property))
                continue;

            takenFields.Add(binding.Field);
            picked.Add(binding);
        }

        var order = properties.Select((property, index) => (property, index))
            .ToDictionary(item => item.property, item => item.index, SymbolEqualityComparer.Default);

        return picked.OrderBy(binding => order[binding.Property]).ToList();
    }

    private static bool IsSameName(string property, string field) =>
        string.Equals(property, field, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Strip(property), Strip(field), StringComparison.Ordinal);

    private static string Strip(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var prefix in StripPrefixes)
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return lower.Substring(prefix.Length);

        return lower;
    }

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool HasAttribute(ISymbol symbol, string name) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(syntax => syntax.Modifiers.Any(SyntaxKind.PartialKeyword));

    private sealed class Binding(IPropertySymbol property, IFieldSymbol field)
    {
        public IPropertySymbol Property { get; } = property;
        public IFieldSymbol Field { get; } = field;
    }
}
