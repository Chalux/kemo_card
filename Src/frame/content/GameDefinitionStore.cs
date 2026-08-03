using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class GameDefinitionStore
{
    private readonly Dictionary<string, CharacterDto> _characters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnemyDto> _enemies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BattleDto> _battles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EventDto> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemDto> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CardDto> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillDto> _skills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BuffDto> _buffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectDto> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttributeDefDto> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameplayEffectDefDto> _gameplayEffects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameplayTagDefDto> _gameplayTags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillActionDto> _skillActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoryDto> _stories = new(StringComparer.Ordinal);

    public bool TryGetCharacter(string id, out CharacterDto dto) => _characters.TryGetValue(id, out dto!);

    public bool TryGetEnemy(string id, out EnemyDto dto) => _enemies.TryGetValue(id, out dto!);

    public bool TryGetBattle(string id, out BattleDto dto) => _battles.TryGetValue(id, out dto!);

    public bool TryGetEvent(string id, out EventDto dto) => _events.TryGetValue(id, out dto!);

    public bool TryGetItem(string id, out ItemDto dto) => _items.TryGetValue(id, out dto!);

    public bool TryGetCard(string id, out CardDto dto) => _cards.TryGetValue(id, out dto!);

    public bool TryGetSkill(string id, out SkillDto dto) => _skills.TryGetValue(id, out dto!);

    public bool TryGetBuff(string id, out BuffDto dto) => _buffs.TryGetValue(id, out dto!);

    public bool TryGetEffect(string id, out EffectDto dto) => _effects.TryGetValue(id, out dto!);

    public bool TryGetAttribute(string id, out AttributeDefDto dto) => _attributes.TryGetValue(id, out dto!);

    public bool TryGetGameplayEffect(string id, out GameplayEffectDefDto dto) => _gameplayEffects.TryGetValue(id, out dto!);

    public bool TryGetGameplayTag(string id, out GameplayTagDefDto dto) => _gameplayTags.TryGetValue(id, out dto!);

    public bool TryGetSkillAction(string id, out SkillActionDto dto) => _skillActions.TryGetValue(id, out dto!);

    public bool TryGetStory(string id, out StoryDto dto) => _stories.TryGetValue(id, out dto!);

    public IReadOnlyDictionary<string, CharacterDto> Characters => _characters;

    public IReadOnlyDictionary<string, EnemyDto> Enemies => _enemies;

    public IReadOnlyDictionary<string, BattleDto> Battles => _battles;

    public IReadOnlyDictionary<string, EventDto> Events => _events;

    public IReadOnlyDictionary<string, ItemDto> Items => _items;

    public IReadOnlyDictionary<string, CardDto> Cards => _cards;

    public IReadOnlyDictionary<string, SkillDto> Skills => _skills;

    public IReadOnlyDictionary<string, BuffDto> Buffs => _buffs;

    public IReadOnlyDictionary<string, EffectDto> Effects => _effects;

    public IReadOnlyDictionary<string, AttributeDefDto> Attributes => _attributes;

    public IReadOnlyDictionary<string, GameplayEffectDefDto> GameplayEffects => _gameplayEffects;

    public IReadOnlyDictionary<string, GameplayTagDefDto> GameplayTags => _gameplayTags;

    public IReadOnlyDictionary<string, SkillActionDto> SkillActions => _skillActions;

    public IReadOnlyDictionary<string, StoryDto> Stories => _stories;

    public bool Contains(EContentCategory category, string id) => category switch
    {
        EContentCategory.Character => _characters.ContainsKey(id),
        EContentCategory.Enemy => _enemies.ContainsKey(id),
        EContentCategory.Battle => _battles.ContainsKey(id),
        EContentCategory.Event => _events.ContainsKey(id),
        EContentCategory.Item => _items.ContainsKey(id),
        EContentCategory.Card => _cards.ContainsKey(id),
        EContentCategory.Skill => _skills.ContainsKey(id),
        EContentCategory.Buff => _buffs.ContainsKey(id),
        EContentCategory.Effect => _effects.ContainsKey(id),
        EContentCategory.Attribute => _attributes.ContainsKey(id),
        EContentCategory.GameplayEffect => _gameplayEffects.ContainsKey(id),
        EContentCategory.GameplayTag => _gameplayTags.ContainsKey(id),
        EContentCategory.SkillAction => _skillActions.ContainsKey(id),
        EContentCategory.Story => _stories.ContainsKey(id),
        _ => false,
    };

    internal void Clear()
    {
        _characters.Clear();
        _enemies.Clear();
        _battles.Clear();
        _events.Clear();
        _items.Clear();
        _cards.Clear();
        _skills.Clear();
        _buffs.Clear();
        _effects.Clear();
        _attributes.Clear();
        _gameplayEffects.Clear();
        _gameplayTags.Clear();
        _skillActions.Clear();
        _stories.Clear();
    }

    internal void Remove(EContentCategory category, string id)
    {
        switch (category)
        {
            case EContentCategory.Character:
                _characters.Remove(id);
                break;
            case EContentCategory.Enemy:
                _enemies.Remove(id);
                break;
            case EContentCategory.Battle:
                _battles.Remove(id);
                break;
            case EContentCategory.Event:
                _events.Remove(id);
                break;
            case EContentCategory.Item:
                _items.Remove(id);
                break;
            case EContentCategory.Card:
                _cards.Remove(id);
                break;
            case EContentCategory.Skill:
                _skills.Remove(id);
                break;
            case EContentCategory.Buff:
                _buffs.Remove(id);
                break;
            case EContentCategory.Effect:
                _effects.Remove(id);
                break;
            case EContentCategory.Attribute:
                _attributes.Remove(id);
                break;
            case EContentCategory.GameplayEffect:
                _gameplayEffects.Remove(id);
                break;
            case EContentCategory.GameplayTag:
                _gameplayTags.Remove(id);
                break;
            case EContentCategory.SkillAction:
                _skillActions.Remove(id);
                break;
            case EContentCategory.Story:
                _stories.Remove(id);
                break;
        }
    }

    internal Dictionary<string, CharacterDto> CharactersMutable => _characters;

    internal Dictionary<string, EnemyDto> EnemiesMutable => _enemies;

    internal Dictionary<string, BattleDto> BattlesMutable => _battles;

    internal Dictionary<string, EventDto> EventsMutable => _events;

    internal Dictionary<string, ItemDto> ItemsMutable => _items;

    internal Dictionary<string, CardDto> CardsMutable => _cards;

    internal Dictionary<string, SkillDto> SkillsMutable => _skills;

    internal Dictionary<string, BuffDto> BuffsMutable => _buffs;

    internal Dictionary<string, EffectDto> EffectsMutable => _effects;

    internal Dictionary<string, AttributeDefDto> AttributesMutable => _attributes;

    internal Dictionary<string, GameplayEffectDefDto> GameplayEffectsMutable => _gameplayEffects;

    internal Dictionary<string, GameplayTagDefDto> GameplayTagsMutable => _gameplayTags;

    internal Dictionary<string, SkillActionDto> SkillActionsMutable => _skillActions;

    internal Dictionary<string, StoryDto> StoriesMutable => _stories;
}