using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class GameDefinitionRegistryOwnerTests
{
    [Test]
    public void TryGetOwnerModId_returns_winner_mod_after_merge()
    {
        var registry = new GameDefinitionRegistry();
        var baseEffects = new Dictionary<string, EffectDto>(StringComparer.Ordinal)
        {
            ["fx_a"] = new() { Id = "fx_a", Kind = EEffectKind.Damage },
        };
        var addonEffects = new Dictionary<string, EffectDto>(StringComparer.Ordinal)
        {
            ["fx_a"] = new() { Id = "fx_a", Kind = EEffectKind.Damage },
        };
        var baseBundle = ContentModTestHelper.Bundle(
            "base.game",
            ModDefinitionsBundle.Empty with { Effects = baseEffects });
        var addonBundle = ContentModTestHelper.Bundle(
            "addon.mod",
            ModDefinitionsBundle.Empty with { Effects = addonEffects });

        registry.Rebuild(new[] { baseBundle, addonBundle }, out _);

        Assert.That(registry.TryGetOwnerModId(EContentCategory.Effect, "fx_a", out var modId), Is.True);
        Assert.That(modId, Is.EqualTo("base.game"));
    }
}