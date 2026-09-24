using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>牌堆（卡组 / 墓地）：标题 + 张数。标题键在场景里配置。</summary>
public partial class CardPileCmp : BaseCmp
{
    [Export] private Label? _lblTitle;
    [Export] private Label? _lblCount;
    [Export] public string TitleKey { get; set; } = "";

    protected override void OnReady()
    {
        if (_lblTitle != null && !string.IsNullOrWhiteSpace(TitleKey))
            _lblTitle.Text = Localization.Tr(TitleKey);
    }

    public void SetCount(int count)
    {
        if (_lblCount != null)
            _lblCount.Text = count.ToString();
    }
}