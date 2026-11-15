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
///     Writes the singleton plumbing into a <c>[Singleton]</c> partial class: <c>Instance</c>,
///     <c>IsInitialized</c>, the shared <c>Log</c> field, a private constructor and the registry entry.
///     The class keeps its own base class and is free to inherit anything else.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SingletonGenerator : IIncrementalGenerator
{
    private const string Attribute = "VRCVideoCacher.Utils.SingletonAttribute";
    private const string Marker = "VRCVideoCacher.Utils.ISingleton";
    private const string Registry = "VRCVideoCacher.Utils.SingletonRegistry";
    private const string Logger = "Serilog.ILogger";
    private const string ProgramLogger = "VRCVideoCacher.Program.Logger";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "VVC3001",
        "Singleton class must be partial",
        "'{0}' must be a partial class so the generator can add the singleton members",
        "SingletonGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        "VVC3002",
        "Singleton class is not supported",
        "'{0}' must be a top-level, non-generic, non-static, non-abstract class",
        "SingletonGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor NeedsParameterlessConstructor = new(
        "VVC3003",
        "Singleton needs a parameterless constructor",
        "'{0}' has constructors but none takes zero parameters, so the generated Instance cannot create it",
        "SingletonGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor ExistingInstance = new(
        "VVC3004",
        "Singleton class already declares Instance",
        "'{0}' already declares an Instance member; remove it and let the generator write the singleton",
        "SingletonGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor ConstructorMustBePrivate = new(
        "VVC3005",
        "Singleton constructor must be private",
        "the constructor of '{0}' must be private so new cannot create a second instance; use {0}.Instance",
        "SingletonGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor RedundantSealed = new(
        "VVC3006",
        "Singleton class is sealed by the generator",
        "'{0}' does not need the sealed modifier; the generated part seals it",
        "SingletonGenerator",
        DiagnosticSeverity.Warning,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(
                Attribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static type => type is not null)
            .Select(static (type, _) => type!);

        context.RegisterSourceOutput(types.Combine(context.CompilationProvider),
            static (output, item) => Emit(output, item.Left, item.Right));
    }

    private static void Emit(SourceProductionContext context, INamedTypeSymbol type, Compilation compilation)
    {
        var diagnostics = new List<Diagnostic>();

        if (type.ContainingType is not null || type.TypeParameters.Length > 0 || type.IsStatic ||
            type.IsAbstract || type.IsRecord || type.TypeKind != TypeKind.Class)
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedType,
                type.Locations.FirstOrDefault(), type.Name));
            return;
        }

        if (!IsPartial(type))
            diagnostics.Add(Diagnostic.Create(NotPartial, type.Locations.FirstOrDefault(), type.Name));

        var constructors = type.InstanceConstructors.Where(item => !item.IsImplicitlyDeclared).ToArray();
        var parameterless = constructors.FirstOrDefault(item => item.Parameters.Length == 0);

        if (constructors.Length > 0 && parameterless is null)
            diagnostics.Add(Diagnostic.Create(NeedsParameterlessConstructor,
                type.Locations.FirstOrDefault(), type.Name));

        foreach (var constructor in constructors.Where(item =>
                     item.DeclaredAccessibility != Accessibility.Private))
            diagnostics.Add(Diagnostic.Create(ConstructorMustBePrivate,
                constructor.Locations.FirstOrDefault(), type.Name));

        if (HasMember(type, "Instance"))
            diagnostics.Add(Diagnostic.Create(ExistingInstance, type.Locations.FirstOrDefault(), type.Name));

        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic);

        if (type.IsSealed)
            context.ReportDiagnostic(Diagnostic.Create(RedundantSealed,
                type.Locations.FirstOrDefault(), type.Name));

        if (diagnostics.Count > 0)
            return;

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var hasLogger = compilation.GetTypeByMetadataName(Logger) is not null;
        var hasBaseLog = HasMember(type, "Log");

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
        {
            builder.AppendLine($"namespace {ns.ToDisplayString()}");
            builder.AppendLine("{");
        }

        var accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var indent = type.ContainingNamespace is { IsGlobalNamespace: false } ? "    " : "";

        builder.AppendLine($"{indent}{accessibility} sealed partial class {type.Name} : global::{Marker}");
        builder.AppendLine($"{indent}{{");
        builder.AppendLine($"{indent}    private static readonly {typeName} InstanceValue = Create();");
        builder.AppendLine($"{indent}    public static {typeName} Instance => InstanceValue;");
        builder.AppendLine($"{indent}    public static bool IsInitialized => InstanceValue is not null;");

        if (hasLogger && !hasBaseLog)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"{indent}    private readonly global::{Logger} Log = global::{ProgramLogger}.ForContext<{typeName}>();");
        }

        builder.AppendLine();
        builder.AppendLine($"{indent}    private static {typeName} Create()");
        builder.AppendLine($"{indent}    {{");
        builder.AppendLine($"{indent}        var instance = new {typeName}();");
        builder.AppendLine($"{indent}        global::{Registry}.Register(instance);");
        builder.AppendLine($"{indent}        return instance;");
        builder.AppendLine($"{indent}    }}");

        if (constructors.Length == 0)
        {
            builder.AppendLine();
            builder.AppendLine($"{indent}    private {type.Name}()");
            builder.AppendLine($"{indent}    {{");
            builder.AppendLine($"{indent}    }}");
        }

        builder.AppendLine($"{indent}}}");

        if (type.ContainingNamespace is { IsGlobalNamespace: false })
            builder.AppendLine("}");

        context.AddSource($"{type.ToDisplayString().Replace('.', '_')}.Singleton.g.cs",
            SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(syntax => syntax.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool HasMember(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.GetMembers(name).Length > 0)
                return true;

        return false;
    }
}
