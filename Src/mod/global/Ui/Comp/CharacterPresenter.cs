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
            AppLog.Warning(
                $"CharacterPresenter: no Neutral portrait. id={character.Id}",
                "CharacterPresenter");
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

    #endregion
}
