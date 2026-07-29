using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class PortraitResolverTests
{
    private static CharacterDto Char(
        string? artPath = null,
        CharacterPortraitsDto? portraits = null) => new()
        {
            Id = "kemo",
            ArtPath = artPath ?? "",
            Portraits = portraits,
        };

    [Test]
    public void ArtPath_only_acts_as_neutral()
    {
        var c = Char(artPath: "chars/kemo.png");
        Assert.That(
            PortraitResolver.ResolveByKey(c, nameof(EPortraitKey.Neutral), _ => true),
            Is.EqualTo("chars/kemo.png"));
    }

    [Test]
    public void Missing_route_falls_back_to_neutral()
    {
        var c = Char(portraits: new CharacterPortraitsDto
        {
            Entries =
            [
                new() { Key = "Neutral", Path = "n.png" },
                new() { Key = "Happy", Path = "h.png" },
            ],
            Routes = new Dictionary<string, string> { ["intro"] = "Happy" },
        });
        Assert.That(PortraitResolver.ResolveByRoute(c, "missing", p => p is "n.png" or "h.png"), Is.EqualTo("n.png"));
    }

    [Test]
    public void Route_target_missing_file_falls_back_to_neutral()
    {
        var c = Char(portraits: new CharacterPortraitsDto
        {
            Entries =
            [
                new() { Key = "Neutral", Path = "n.png" },
                new() { Key = "Happy", Path = "h.png" },
            ],
            Routes = new Dictionary<string, string> { ["intro"] = "Happy" },
        });
        Assert.That(PortraitResolver.ResolveByRoute(c, "intro", p => p == "n.png"), Is.EqualTo("n.png"));
    }

    [Test]
    public void Custom_key_route_works()
    {
        var c = Char(portraits: new CharacterPortraitsDto
        {
            Entries =
            [
                new() { Key = "Neutral", Path = "n.png" },
                new() { Key = "custom:wave", Path = "w.png" },
            ],
            Routes = new Dictionary<string, string> { ["hi"] = "custom:wave" },
        });
        Assert.That(PortraitResolver.ResolveByRoute(c, "hi", _ => true), Is.EqualTo("w.png"));
    }
}