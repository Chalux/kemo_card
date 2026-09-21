using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 出货内容（<c>Config/mods/base-game</c>）的加载辅助：真实内容此前没有任何覆盖，
/// 卡牌 / buff / 球的接线写错不会被合成 DTO 的用例发现。
/// </summary>
internal static class BaseGameContent
{
    public static ModDefinitionsBundle Load()
    {
        var modFolder = LocateFolder();
        var manifestPath = Path.Combine(modFolder, "mod.json");
        Assert.That(File.Exists(manifestPath), Is.True, $"找不到 base-game 清单:{manifestPath}");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<ContentModManifestDto>(
            File.ReadAllText(manifestPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.That(manifest, Is.Not.Null);

        return ContentModLoader.Load(new DiscoveredModEntry(modFolder, manifest!)).Definitions;
    }

    /// <summary>
    /// 复刻 <c>ModFactory.Bootstrap</c> 的注册顺序：条件域必须在内容 Rebuild 之前填好，
    /// 否则 <c>Story.unlock</c> 的 CondType 校验会因注册表为空而误报未知名。
    /// </summary>
    public static GameDefinitionRegistry BuildRegistry(out ContentLoadReport report)
    {
        RegisterBuiltinConditions();
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("base.game", Load())], out report);
        return registry;
    }

    public static GameDefinitionRegistry BuildRegistry() => BuildRegistry(out _);

    public static void RegisterBuiltinConditions()
    {
        ConditionDomains.Persistent.Clear();
        ConditionDomains.Combat.Clear();
        BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
        BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);
    }

    private static string LocateFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Config", "mods", "base-game");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("从测试输出目录向上找不到 Config/mods/base-game。");
    }
}
