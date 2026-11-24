using System.Globalization;
using System.Text;
using KemoCard.Mod.Combat.Marble;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 待发队列的文本展示（顺序 + 点数 + 半径）。与 Godot 解耦，便于单测；
/// 文案键由调用方传入 <c>Localization.Tr</c>。
/// </summary>
public static class MarbleQueueText
{
    /// <summary>单颗球的展示行；<paramref name="displayIndex"/> 为 1 起的队列序号。</summary>
    public static string FormatEntry(int displayIndex, MarbleBallSpec spec, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        return string.Format(
            CultureInfo.InvariantCulture,
            translate("UI_MARBLE_QUEUE_ENTRY"),
            displayIndex,
            spec.Points,
            spec.Radius);
    }

    /// <summary>整个队列按 FIFO 顺序逐行展示；空队列返回空队列文案。</summary>
    public static string Format(MarbleQueue queue, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(translate);
        if (queue.Count == 0)
            return translate("UI_MARBLE_QUEUE_EMPTY");

        var builder = new StringBuilder();
        for (var i = 0; i < queue.Count; i++)
        {
            if (i > 0)
                builder.Append('\n');
            builder.Append(FormatEntry(i + 1, queue[i], translate));
        }

        return builder.ToString();
    }
}
