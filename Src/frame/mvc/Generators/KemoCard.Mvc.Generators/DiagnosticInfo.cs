using Microsoft.CodeAnalysis;

namespace KemoCard.Mvc.Generators;

/// <summary>
/// 延迟生成 <see cref="Diagnostic"/> 的轻量载体，便于在生成管线中携带诊断信息。
/// </summary>
internal sealed class DiagnosticInfo(DiagnosticDescriptor descriptor, Location location, params object[] messageArgs)
{
    private readonly DiagnosticDescriptor _descriptor = descriptor;
    private readonly Location _location = location;
    private readonly object[] _messageArgs = messageArgs;

    public Diagnostic ToDiagnostic() => Diagnostic.Create(_descriptor, _location, _messageArgs);

    public static Location From(ISymbol symbol)
    {
        var locations = symbol.Locations;
        return locations.Length > 0 ? locations[0] : Location.None;
    }
}