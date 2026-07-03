using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace KemoCard.Mvc.Generators;

/// <summary>
/// 解析带 <c>[EventTable]</c> 的 partial 表类与其枚举上的 <c>[EventPayload]</c> 标注，
/// 生成 <c>EventKey</c> 静态字段以及对应 Mod 的 On*/Notify* 包装方法。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EventTableGenerator : IIncrementalGenerator
{
    private const string EventTableAttributeName = "KemoCard.Frame.Mvc.EventTableAttribute";
    private const string EventPayloadAttributeName = "KemoCard.Frame.Mvc.EventPayloadAttribute";
    private const string BaseModFullName = "KemoCard.Frame.Mvc.BaseMod";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "KMV001",
        "事件表必须是 partial",
        "事件表 '{0}' 标注了 [EventTable]，但未声明为 partial，无法生成 EventKey 字段",
        "KemoCard.Mvc",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotEnum = new(
        "KMV002",
        "EventTable 的 EnumType 必须是枚举",
        "事件表 '{0}' 的 EnumType 参数 '{1}' 不是枚举类型",
        "KemoCard.Mvc",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NotBaseMod = new(
        "KMV003",
        "EventTable 的 ModType 必须派生自 BaseMod",
        "事件表 '{0}' 的 ModType 参数 '{1}' 不是 KemoCard.Frame.Mvc.BaseMod 的子类",
        "KemoCard.Mvc",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoEvents = new(
        "KMV004",
        "事件表没有可生成的事件",
        "事件表 '{0}' 对应的枚举 '{1}' 没有任何带 [EventPayload] 的成员",
        "KemoCard.Mvc",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.ForAttributeWithMetadataName(
            EventTableAttributeName,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => Transform(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            foreach (var diagnostic in model.Diagnostics)
            {
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            }
            if (model.TableSource is not null)
            {
                spc.AddSource(model.TableHintName!, SourceText.From(model.TableSource, Encoding.UTF8));
            }
            if (model.ModSource is not null)
            {
                spc.AddSource(model.ModHintName!, SourceText.From(model.ModSource, Encoding.UTF8));
            }
        });
    }

    private static TableGen? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol tableSymbol)
        {
            return null;
        }

        var attribute = ctx.Attributes[0];
        if (attribute.ConstructorArguments.Length < 2)
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();
        var tableDisplay = tableSymbol.ToDisplayString();
        var location = DiagnosticInfo.From(tableSymbol);

        var isPartial = false;
        foreach (var reference in tableSymbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is ClassDeclarationSyntax cds &&
                cds.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
            {
                isPartial = true;
                break;
            }
        }
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(NotPartial, location, tableDisplay));
        }

        var enumSymbol = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        var modSymbol = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;

        if (enumSymbol is null || enumSymbol.TypeKind != TypeKind.Enum)
        {
            diagnostics.Add(new DiagnosticInfo(NotEnum, location, tableDisplay, enumSymbol?.ToDisplayString() ?? "<null>"));
            return new TableGen(diagnostics);
        }

        if (modSymbol is null || !DerivesFromBaseMod(modSymbol))
        {
            diagnostics.Add(new DiagnosticInfo(NotBaseMod, location, tableDisplay, modSymbol?.ToDisplayString() ?? "<null>"));
            return new TableGen(diagnostics);
        }

        var events = new List<EventEntry>();
        var enumFq = enumSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var member in enumSymbol.GetMembers())
        {
            if (member is not IFieldSymbol { IsConst: true } field)
            {
                continue;
            }
            ITypeSymbol? payload = null;
            foreach (var attr in field.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == EventPayloadAttributeName &&
                    attr.ConstructorArguments.Length == 1)
                {
                    payload = attr.ConstructorArguments[0].Value as ITypeSymbol;
                    break;
                }
            }
            if (payload is null)
            {
                continue;
            }
            events.Add(new EventEntry(
                field.Name,
                payload.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                $"{enumFq}.{field.Name}"));
        }

        if (events.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(NoEvents, location, tableDisplay, enumSymbol.ToDisplayString()));
            return new TableGen(diagnostics);
        }

        if (!isPartial)
        {
            return new TableGen(diagnostics);
        }

        var tableNamespace = tableSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : tableSymbol.ContainingNamespace.ToDisplayString();
        var modNamespace = modSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : modSymbol.ContainingNamespace.ToDisplayString();
        var tableFq = tableSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var tableSource = BuildTableSource(tableNamespace, tableSymbol.Name, events);
        var modSource = BuildModSource(modNamespace, modSymbol.Name, tableFq, events);

        return new TableGen(diagnostics)
        {
            TableHintName = $"{tableSymbol.Name}.EventKeys.g.cs",
            TableSource = tableSource,
            ModHintName = $"{tableSymbol.Name}.{modSymbol.Name}.ModEvents.g.cs",
            ModSource = modSource,
        };
    }

    private static bool DerivesFromBaseMod(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == BaseModFullName)
            {
                return true;
            }
        }
        return false;
    }

    private static string BuildTableSource(string? ns, string tableName, List<EventEntry> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        var indent = AppendNamespaceOpen(sb, ns);
        sb.AppendLine($"{indent}static partial class {tableName}");
        sb.AppendLine($"{indent}{{");
        foreach (var e in events)
        {
            sb.AppendLine($"{indent}    public static readonly global::KemoCard.Frame.Mvc.EventKey<{e.PayloadFq}> {e.Name}");
            sb.AppendLine($"{indent}        = new((int){e.EnumMemberFq});");
        }
        sb.AppendLine($"{indent}}}");
        AppendNamespaceClose(sb, ns);
        return sb.ToString();
    }

    private static string BuildModSource(string? ns, string modName, string tableFq, List<EventEntry> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        var indent = AppendNamespaceOpen(sb, ns);
        sb.AppendLine($"{indent}partial class {modName}");
        sb.AppendLine($"{indent}{{");
        foreach (var e in events)
        {
            var listener = $"global::KemoCard.Frame.Mvc.IEventListener<{e.PayloadFq}>";
            sb.AppendLine($"{indent}    public {listener} On{e.Name}(");
            sb.AppendLine($"{indent}        global::System.Action<{e.PayloadFq}, {listener}> handler,");
            sb.AppendLine($"{indent}        object? caller = null)");
            sb.AppendLine($"{indent}        => InternalBus.On({tableFq}.{e.Name}, handler, caller);");
            sb.AppendLine();
            sb.AppendLine($"{indent}    public void Notify{e.Name}({e.PayloadFq} payload)");
            sb.AppendLine($"{indent}        => InternalBus.Send({tableFq}.{e.Name}, payload);");
            sb.AppendLine();
        }
        sb.AppendLine($"{indent}}}");
        AppendNamespaceClose(sb, ns);
        return sb.ToString();
    }

    private static string AppendNamespaceOpen(StringBuilder sb, string? ns)
    {
        if (ns is null)
        {
            return string.Empty;
        }
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
        return "    ";
    }

    private static void AppendNamespaceClose(StringBuilder sb, string? ns)
    {
        if (ns is not null)
        {
            sb.AppendLine("}");
        }
    }

    private readonly struct EventEntry(string name, string payloadFq, string enumMemberFq)
    {
        public string Name { get; } = name;
        public string PayloadFq { get; } = payloadFq;
        public string EnumMemberFq { get; } = enumMemberFq;
    }

    private sealed class TableGen
    {
        public TableGen(List<DiagnosticInfo> diagnostics)
        {
            Diagnostics = diagnostics.ToImmutableArray();
        }

        public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
        public string? TableHintName { get; set; }
        public string? TableSource { get; set; }
        public string? ModHintName { get; set; }
        public string? ModSource { get; set; }
    }
}
