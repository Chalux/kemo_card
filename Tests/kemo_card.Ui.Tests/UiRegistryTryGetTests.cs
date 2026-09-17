using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class UiRegistryTryGetTests
{
    [Test]
    public void Get_unknown_id_returns_null()
    {
        var reg = new UIRuntimeRegistry();
        Assert.That(reg.Get("missing"), Is.Null);
    }

    [Test]
    public void Get_registered_id_returns_entry()
    {
        var reg = new UIRuntimeRegistry();
        reg.Register(new UIRuntimeEntry
        {
            OwnerModId = "test.mod",
            Id = "testUI",
            Dir = "ui/test",
            Type = KemoCard.Frame.UI.Def.EUIType.Dlg
        });

        var entry = reg.Get("testUI");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Id, Is.EqualTo("testUI"));
        Assert.That(entry.Dir, Is.EqualTo("ui/test"));
    }
}