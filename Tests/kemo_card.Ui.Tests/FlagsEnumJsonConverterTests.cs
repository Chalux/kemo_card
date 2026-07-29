using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class FlagsEnumJsonConverterTests
{
    private static JsonSerializerOptions Opts()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new FlagsEnumJsonConverter<ERace>());
        return o;
    }

    private sealed class Wrap
    {
        public ERace Race { get; init; }
    }

    [Test]
    public void Deserialize_single_string()
    {
        var w = JsonSerializer.Deserialize<Wrap>("""{"race":"Human"}""", Opts());
        Assert.That(w!.Race, Is.EqualTo(ERace.Human));
    }

    [Test]
    public void Deserialize_string_array_ors_flags()
    {
        var w = JsonSerializer.Deserialize<Wrap>("""{"race":["Human","Canine"]}""", Opts());
        Assert.That(w!.Race, Is.EqualTo(ERace.Human | ERace.Canine));
    }
}