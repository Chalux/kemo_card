using System.Globalization;
using System.Text;

namespace KemoCard.Frame.Content;

public sealed class GameDefinitionRegistry
{
    /// <summary>FNV-1a 64 位偏移基数。</summary>
    private const ulong FnvOffsetBasis = 14695981039346656037UL;

    /// <summary>FNV-1a 64 位质数。</summary>
    private const ulong FnvPrime = 1099511628211UL;

    private readonly object _gate = new();
    private readonly Dictionary<(EContentCategory Category, string Id), string> _ownerModIds = new();

    public GameDefinitionStore Store { get; } = new();

    public int DefinitionVersion { get; private set; }

    /// <summary>
    /// 当前生效定义集的内容版本指纹（16 位小写十六进制，FNV-1a 64 位）。
    /// 载荷按 <see cref="EContentCategory"/> 声明顺序逐类别写出「类别=数量\n」，随后每行一个该类别按
    /// 序数排序的定义 id，因此与 Mod 注册顺序、字典枚举顺序无关；只取 id，与 DTO 内容无关。
    /// 必须使用稳定哈希：<c>string.GetHashCode()</c> / <c>HashCode.Combine</c> 的字符串哈希带
    /// 进程级随机种子，跨会话不可比较，而本指纹要写进全局存档用于检测内容变更
    /// （内容 Mod 规格 §5：与 <see cref="DefinitionVersion"/> 联动）。
    /// 未执行过 <see cref="Rebuild"/> 时为空定义集的指纹。
    /// </summary>
    public string ContentVersionHash { get; private set; } =
        ComputeFnv1a64Hex(BuildFingerprintPayload(static _ => Array.Empty<string>()));

    public void Rebuild(IReadOnlyList<ModContentBundle> bundles, out ContentLoadReport report)
    {
        lock (_gate)
        {
            var merger = new ContentRegistryMerger();
            var mergeResult = merger.Merge(bundles, Store);

            _ownerModIds.Clear();
            foreach (var (key, modId) in mergeResult.OwnerModIds)
            {
                _ownerModIds[key] = modId;
            }

            var validator = new ContentDefinitionValidator();
            var foundValidationErrors = validator.Validate(Store);
            var removedValidationErrors = Array.Empty<ContentDefinitionValidationError>();
            if (foundValidationErrors.Count > 0)
            {
                RemoveInvalidDefinitions(foundValidationErrors);
                removedValidationErrors = foundValidationErrors.ToArray();
            }

            DefinitionVersion++;
            ContentVersionHash = ComputeFnv1a64Hex(BuildFingerprintPayload(CollectSortedIds));
            report = new ContentLoadReport(
                Array.Empty<ModSkipEntry>(),
                mergeResult.IdConflicts,
                Array.Empty<ContentDefinitionValidationError>(),
                Array.Empty<ScriptLoadError>(),
                removedValidationErrors);
        }
    }

    public bool Contains(EContentCategory category, string id)
    {
        lock (_gate)
        {
            return Store.Contains(category, id);
        }
    }

    public bool TryGetOwnerModId(EContentCategory category, string id, out string modId)
    {
        lock (_gate)
        {
            return _ownerModIds.TryGetValue((category, id), out modId!);
        }
    }

    /// <summary>
    /// 按内容类别取当前生效定义集的 id，并按序数排序；排序保证指纹与注册顺序无关。
    /// </summary>
    private IReadOnlyList<string> CollectSortedIds(EContentCategory category)
    {
        IEnumerable<string> ids = category switch
        {
            EContentCategory.Character => Store.Characters.Keys,
            EContentCategory.Enemy => Store.Enemies.Keys,
            EContentCategory.Battle => Store.Battles.Keys,
            EContentCategory.Event => Store.Events.Keys,
            EContentCategory.Card => Store.Cards.Keys,
            EContentCategory.Item => Store.Items.Keys,
            EContentCategory.Skill => Store.Skills.Keys,
            EContentCategory.Buff => Store.Buffs.Keys,
            EContentCategory.Effect => Store.Effects.Keys,
            EContentCategory.SkillAction => Store.SkillActions.Keys,
            EContentCategory.Attribute => Store.Attributes.Keys,
            EContentCategory.GameplayEffect => Store.GameplayEffects.Keys,
            EContentCategory.GameplayTag => Store.GameplayTags.Keys,
            EContentCategory.Story => Store.Stories.Keys,
            EContentCategory.OrbType => Store.OrbTypes.Keys,
            _ => Array.Empty<string>(),
        };

        var sorted = ids.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    /// <summary>
    /// 拼接内容指纹载荷：按 <see cref="EContentCategory"/> 声明顺序逐类别一行「类别=数量」，
    /// 随后每行一个该类别已排序的定义 id。
    /// </summary>
    private static string BuildFingerprintPayload(Func<EContentCategory, IReadOnlyList<string>> idsByCategory)
    {
        var payload = new StringBuilder();
        foreach (var category in Enum.GetValues<EContentCategory>())
        {
            var ids = idsByCategory(category);
            payload.Append(category.ToString()).Append('=').Append(ids.Count).Append('\n');
            foreach (var id in ids)
            {
                payload.Append(id).Append('\n');
            }
        }

        return payload.ToString();
    }

    /// <summary>
    /// FNV-1a 64 位哈希（输入按 UTF-8 取字节），返回 16 位小写十六进制字符串。
    /// </summary>
    private static string ComputeFnv1a64Hex(string payload)
    {
        var hash = FnvOffsetBasis;
        foreach (var value in Encoding.UTF8.GetBytes(payload))
        {
            hash = unchecked((hash ^ value) * FnvPrime);
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private void RemoveInvalidDefinitions(IReadOnlyList<ContentDefinitionValidationError> errors)
    {
        foreach (var error in errors)
        {
            Store.Remove(error.Category, error.DefinitionId);
            _ownerModIds.Remove((error.Category, error.DefinitionId));
        }
    }
}