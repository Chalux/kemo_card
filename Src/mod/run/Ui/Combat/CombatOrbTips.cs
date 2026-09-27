using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Glossary;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 充能球位的悬停文案（2026-09-27）：标题 = 球名；正文 = 触发效果 + 产球者。
/// 效果正文与词典共用同一份键（<c>UI_GLOSSARY_ORB_BODY_*</c>），改文案只改 CSV。
/// </summary>
/// <remarks>
/// 纯函数（可单测）：取 <paramref name="translate"/> 与 <paramref name="resolveProducerName"/> 委托，
/// 不直接依赖 Godot。产球者取 <c>OrbInstance.ProducerIndex</c>（授予时记录）；
/// <c>&lt; 0</c>（无产球者，触发时按全队最高攻击者解析）由解析器回空串 → 回落"触发时按全队最高攻击者"。
/// </remarks>
public static class CombatOrbTips
{
    /// <summary>产球者行（<c>{0}</c> = 角色显示名）。</summary>
    public const string ProducerKey = "UI_COMBAT_ORB_PRODUCER";

    /// <summary>产球者未知 / 无产球者时的行文案。</summary>
    public const string ProducerUnknownKey = "UI_COMBAT_ORB_PRODUCER_UNKNOWN";

    public static (string Title, string Body) Build(
        OrbTypeDto? orbType,
        string orbTypeId,
        int producerIndex,
        Func<int, string> resolveProducerName,
        Func<string, string> translate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orbTypeId);
        ArgumentNullException.ThrowIfNull(resolveProducerName);
        ArgumentNullException.ThrowIfNull(translate);

        var title = orbType is null || string.IsNullOrWhiteSpace(orbType.DisplayNameId)
            ? orbTypeId
            : translate(orbType.DisplayNameId);

        var lines = new List<string>(2);
        if (orbType is not null)
        {
            var key = GlossaryBuilder.ResolveOrbBodyKey(orbType);
            var args = GlossaryBuilder.ResolveOrbBodyArgs(orbType);
            var effect = args is null
                ? translate(key)
                : string.Format(translate(key), args.ToArray());
            if (!string.IsNullOrWhiteSpace(effect))
            {
                lines.Add(effect);
            }
        }

        var producer = resolveProducerName(producerIndex);
        lines.Add(string.IsNullOrWhiteSpace(producer)
            ? translate(ProducerUnknownKey)
            : string.Format(translate(ProducerKey), producer));

        return (title, string.Join("\n", lines));
    }
}
