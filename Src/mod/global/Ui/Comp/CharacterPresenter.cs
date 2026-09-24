using System;
using System.Collections.Generic;
using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui.Comp;

/// <summary>
/// 角色视觉呈现：优先 SpriteFrames（AnimatedSprite2D），否则 Neutral 立绘（TextureRect）。
/// </summary>
public partial class CharacterPresenter : Control
{
    /// <summary>
    /// 已经报过「缺少 Neutral 立绘」的角色 id，保证同一角色只报一次。
    /// </summary>
    /// <remarks>
    /// 悬停预览（<c>RunTeamEditDlg</c> 的角色池）每次鼠标移入都会重新 <see cref="Bind"/>，
    /// 于是「立绘缺失」这个**内容侧的稳定事实**被反复记录：2026-09-24 的一局游戏里
    /// 5 个角色共刷出 690 条警告（每条还要跟 10 行 C# 堆栈），日志被噪声淹没、真错误反而看不见。
    /// 美术补齐前缺失不会改变，因此按 id 去重；补齐后新角色若仍缺失照样会被记下来。
    /// </remarks>
    private static readonly HashSet<string> MissingPortraitWarnedIds = new(StringComparer.Ordinal);
    private static readonly object WarnGate = new();

    [Export] private TextureRect? _portrait;
    [Export] private AnimatedSprite2D? _animSprite;

    private CharacterDto? _character;
    private CharacterPresentationDto? _presentation;
    private SpriteFrames? _frames;
    private IReadOnlyList<string> _animNames = Array.Empty<string>();
    private bool _hasPresentation;

    public bool HasPresentation => _hasPresentation;

    public void Bind(CharacterDto character)
    {
        ArgumentNullException.ThrowIfNull(character);

        _character = character;
        _presentation = character.Presentation;
        ClearPresentation();

        if (_presentation is { Kind: EPresentationKind.Spine })
        {
            AppLog.Warning(
                $"CharacterPresenter: Spine not implemented, fallback to portrait. id={character.Id}",
                "CharacterPresenter");
            BindPortrait(character);
            return;
        }

        if (_presentation is { Kind: EPresentationKind.SpriteFrames }
            && !string.IsNullOrWhiteSpace(_presentation.Path))
        {
            var frames = CharacterArtLoader.TryLoadSpriteFrames(_presentation.Path, character.Id);
            if (frames != null)
            {
                BindSpriteFrames(character, frames, _presentation);
                return;
            }

            AppLog.Warning(
                $"CharacterPresenter: SpriteFrames load failed, fallback to portrait. id={character.Id} path={_presentation.Path}",
                "CharacterPresenter");
        }

        BindPortrait(character);
    }

    public void Play(string animName)
    {
        if (!_hasPresentation || _animSprite == null || _frames == null)
        {
            return;
        }

        var target = animName;
        if (string.IsNullOrWhiteSpace(target) || !_frames.HasAnimation(target))
        {
            target = _presentation?.DefaultAnim ?? "idle";
            if (!_frames.HasAnimation(target))
            {
                AppLog.Warning(
                    $"CharacterPresenter: anim '{animName}' missing and default unavailable. id={_character?.Id}",
                    "CharacterPresenter");
                return;
            }
        }

        _animSprite.Play(target);
    }

    public IReadOnlyList<string> ListAnims() => _animNames;

    #region 绑定分支

    private void BindSpriteFrames(
        CharacterDto character,
        SpriteFrames frames,
        CharacterPresentationDto presentation)
    {
        _frames = frames;
        _hasPresentation = true;
        _animNames = PresentationAnimList.Resolve(frames.GetAnimationNames(), presentation.Anims);

        if (_animSprite != null)
        {
            _animSprite.SpriteFrames = frames;
            _animSprite.Visible = true;
        }

        if (_portrait != null)
        {
            _portrait.Texture = null;
            _portrait.Visible = false;
        }

        Play(presentation.DefaultAnim);
    }

    private void BindPortrait(CharacterDto character)
    {
        ClearPresentation();

        var path = PortraitResolver.ResolveByKey(
            character,
            nameof(EPortraitKey.Neutral),
            relative => CharacterArtLoader.PathExists(relative, character.Id));

        if (string.IsNullOrEmpty(path))
        {
            WarnMissingPortraitOnce(character.Id);
            if (_portrait != null)
            {
                _portrait.Texture = null;
                _portrait.Visible = false;
            }

            return;
        }

        var texture = CharacterArtLoader.TryLoadTexture(path, character.Id);
        if (_portrait != null)
        {
            _portrait.Texture = texture;
            _portrait.Visible = texture != null;
        }

        if (texture == null)
        {
            AppLog.Warning(
                $"CharacterPresenter: portrait texture load failed. id={character.Id} path={path}",
                "CharacterPresenter");
        }
    }

    private void ClearPresentation()
    {
        _frames = null;
        _hasPresentation = false;
        _animNames = Array.Empty<string>();

        if (_animSprite != null)
        {
            _animSprite.Stop();
            _animSprite.SpriteFrames = null;
            _animSprite.Visible = false;
        }
    }

    /// <summary>
    /// 立绘缺失按角色 id 只报一次（去重表见 <see cref="MissingPortraitWarnedIds"/>）。
    /// </summary>
    private static void WarnMissingPortraitOnce(string characterId)
    {
        lock (WarnGate)
        {
            if (!MissingPortraitWarnedIds.Add(characterId))
            {
                return;
            }
        }

        AppLog.Warning(
            $"CharacterPresenter: no Neutral portrait. id={characterId}（同一角色只报一次）",
            "CharacterPresenter");
    }

    #endregion
}