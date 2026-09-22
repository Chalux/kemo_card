using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Condition;
using KemoCard.Mod.Global.Ui.Themes;
using System.Text.Json;

namespace KemoCard.Mod.Run.Ui;

public partial class StorySelectDlg : BaseDlg
{
    [Export] private ItemList? _storyList;
    [Export] private Label? _lblName;
    [Export] private Label? _lblAuthor;
    [Export] private Label? _lblMod;
    [Export] private Label? _lblMode;
    [Export] private Label? _lblDesc;
    [Export] private Label? _lblUnlockHint;
    [Export] private LineEdit? _seedInput;
    [Export] private Button? _btnConfirm;
    [Export] private Button? _btnCancel;

    private readonly List<StoryEntry> _entries = [];
    private int _selectedIndex = -1;
    private GlobalPersistentCondContext? _condContext;

    public override string UIId => RunUiIds.StorySelect;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_storyList != null)
        {
            Binder.OnItemSelected(_storyList, OnStorySelected);
        }

        if (_btnConfirm != null)
        {
            OnClicks(_btnConfirm, OnConfirm);
        }

        if (_btnCancel != null)
        {
            OnClicks(_btnCancel, Close);
        }
    }

    protected override void OnOpen()
    {
        // 已知跨功能读：run 的界面需要 global 的解锁账本来判断故事是否已解锁。
        // 这是纯读且语义上属于「全局进度」，但严格说违反了 ui-mod-binding 规格 §5.4
        // 「界面只取自家门面」。干净解法是引入 frame 级的 IPersistentCondContext 提供者，
        // 尚未落地（见规格 §5.4 与 §12 待办）；在此之前保留此处的直接取用并显式标注。
        _condContext = new GlobalPersistentCondContext(AppRoot.Services.GlobalController);
        RebuildList();
    }

    protected override void UpdateView()
    {
    }

    #region 列表与详情

    private void RebuildList()
    {
        _entries.Clear();
        _storyList?.Clear();

        var registry = AppRoot.Services.ContentModPipeline.Registry;
        foreach (var (id, story) in registry.Store.Stories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var playable = EvaluatePlayable(story);
            var displayName = string.IsNullOrWhiteSpace(story.DisplayNameId)
                ? id
                : Localization.Tr(story.DisplayNameId);
            _storyList?.AddItem(playable ? displayName : $"{Localization.Tr("UI_STORY_LOCKED")} · {displayName}");
            if (_storyList is { } list)
            {
                var index = list.ItemCount - 1;
                if (!playable)
                {
                    list.SetItemCustomBgColor(index, KemoPalette.SurfaceSunken);
                    list.SetItemCustomFgColor(index, KemoPalette.TextDisabled);
                }
            }

            var modId = registry.TryGetOwnerModId(EContentCategory.Story, id, out var owner) ? owner : "?";
            _entries.Add(new StoryEntry(id, story, playable, modId));
        }

        _selectedIndex = -1;
        UpdateDetail();
    }

    private bool EvaluatePlayable(StoryDto story)
    {
        if (story.Unlock is not { } unlock || unlock.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        var registry = ConditionDomains.Persistent;
        if (!ConditionParser.TryParse<IPersistentCondContext>(unlock, registry, $"story:{story.Id}", out var expr, out _) || expr is null)
        {
            return false;
        }

        return ConditionEvaluator.Evaluate(expr, _condContext!, registry).Passed;
    }

    private void OnStorySelected(long index)
    {
        _selectedIndex = (int)index;
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var hasSelection = _selectedIndex >= 0 && _selectedIndex < _entries.Count;
        var entry = hasSelection ? _entries[_selectedIndex] : null;

        if (entry == null)
        {
            ClearDetail();
            if (_btnConfirm != null)
            {
                _btnConfirm.Disabled = true;
            }

            return;
        }

        var story = entry.Story;
        if (_lblName != null)
        {
            _lblName.Text = string.IsNullOrWhiteSpace(story.DisplayNameId) ? entry.Id : Localization.Tr(story.DisplayNameId);
        }

        if (_lblAuthor != null)
        {
            _lblAuthor.Text = string.IsNullOrWhiteSpace(story.Author) ? "-" : story.Author;
        }

        if (_lblMod != null)
        {
            _lblMod.Text = entry.ModId;
        }

        if (_lblMode != null)
        {
            _lblMode.Text = Localization.Tr(story.SinglePlayerOnly ? "UI_STORY_MODE_SINGLE" : "UI_STORY_MODE_COOP");
        }

        if (_lblDesc != null)
        {
            _lblDesc.Text = string.IsNullOrWhiteSpace(story.DescId) ? "-" : Localization.Tr(story.DescId);
        }

        if (_lblUnlockHint != null)
        {
            _lblUnlockHint.Visible = !entry.Playable;
            _lblUnlockHint.Text = entry.Playable ? "" : Localization.Tr("UI_STORY_UNLOCK_REQUIRED");
        }

        if (_btnConfirm != null)
        {
            _btnConfirm.Disabled = !entry.Playable;
        }
    }

    private void ClearDetail()
    {
        if (_lblName != null)
        {
            _lblName.Text = "-";
        }

        if (_lblAuthor != null)
        {
            _lblAuthor.Text = "-";
        }

        if (_lblMod != null)
        {
            _lblMod.Text = "-";
        }

        if (_lblMode != null)
        {
            _lblMode.Text = "-";
        }

        if (_lblDesc != null)
        {
            _lblDesc.Text = "-";
        }

        if (_lblUnlockHint != null)
        {
            _lblUnlockHint.Visible = false;
        }
    }

    #endregion

    #region 确定与取消

    private void OnConfirm()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _entries.Count)
        {
            return;
        }

        var entry = _entries[_selectedIndex];
        if (!entry.Playable)
        {
            return;
        }

        RunRuntime.CreateNew(entry.Id, ParseSeed(), []);
        Close();
        _ = RunUiController.OpenRunMainAsync();
    }

    private int ParseSeed()
    {
        var text = _seedInput?.Text ?? "-1";
        return int.TryParse(text, out var seed) ? seed : -1;
    }

    #endregion

    private sealed record StoryEntry(string Id, StoryDto Story, bool Playable, string ModId);
}