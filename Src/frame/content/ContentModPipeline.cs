using KemoCard.Frame.Scripting;

namespace KemoCard.Frame.Content;

public sealed class ContentModPipeline
{
    private readonly string _modRootDirectory;
    private readonly ContentModDiscovery _discovery = new();
    private readonly ContentModActivationPlanner _planner = new();
    private readonly IContentModLogger _logger;
    private readonly IContentModUserNotifier _notifier;
    private readonly IContentModTranslationLoader _translationLoader;
    private readonly IScriptRuntimeResetter _scriptRuntimeResetter;
    private readonly ModScriptCatalog _scriptCatalog;
    private readonly ModScriptPrewarmer? _scriptPrewarmer;

    public ContentModPipeline(
        string modRootDirectory,
        GameDefinitionRegistry registry,
        IContentModLogger logger,
        IContentModUserNotifier notifier,
        IScriptRuntimeResetter scriptRuntimeResetter,
        ModScriptCatalog scriptCatalog,
        IContentModTranslationLoader? translationLoader = null,
        ModScriptPrewarmer? scriptPrewarmer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modRootDirectory);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(scriptRuntimeResetter);
        ArgumentNullException.ThrowIfNull(scriptCatalog);

        _modRootDirectory = modRootDirectory;
        Registry = registry;
        _logger = logger;
        _notifier = notifier;
        _scriptRuntimeResetter = scriptRuntimeResetter;
        _scriptCatalog = scriptCatalog;
        _translationLoader = translationLoader ?? new NullContentModTranslationLoader();
        _scriptPrewarmer = scriptPrewarmer;
    }

    public GameDefinitionRegistry Registry { get; }

    public ModScriptCatalog ScriptCatalog => _scriptCatalog;

    /// <summary>
    /// 重建准入守卫：内容 Mod 规格 §5 要求「Run 进行中不提供 Mod 开关；若误调用 <see cref="Rebuild"/>，
    /// 拒绝并记日志」。Run 状态属于 mod 层，frame 层不得依赖它，因此这里只留可注入的谓词，
    /// 由组合根（<c>ModFactory</c>）把「无进行中的 Run」接进来；<c>null</c>（默认）表示始终允许重建，
    /// 既有调用方与测试无需改动。
    /// </summary>
    public Func<bool>? CanRebuild { get; set; }

    /// <summary>
    /// 最近一次 <see cref="Rebuild"/> 的结果报告（被拒绝时也写入拒绝原因），
    /// 供组合根 / 后续主菜单 Mod UI 读取（内容 Mod 规格 §6.2 的 notifier 首版为空实现）。
    /// </summary>
    public ContentLoadReport LatestReport { get; private set; } = ContentLoadReport.Empty;

    /// <summary>重建被拒（Run 进行中误调用）时写进报告的占位 mod id：仅在启用集为空、无法逐个列出时使用。</summary>
    private const string RebuildRejectedPlaceholderModId = "*";

    private const string RebuildRejectedDetail =
        "Rebuild rejected: a Run is in progress; content mods cannot be changed during a Run.";

    public ContentLoadReport Rebuild(IReadOnlyList<string> enabledModIds)
    {
        ArgumentNullException.ThrowIfNull(enabledModIds);

        if (CanRebuild is { } canRebuild && !canRebuild())
        {
            return RecordRejectedRebuild(enabledModIds);
        }

        _scriptRuntimeResetter.BeginRebuild();
        try
        {
            LatestReport = RebuildCore(enabledModIds);
            return LatestReport;
        }
        finally
        {
            _scriptRuntimeResetter.EndRebuild();
        }
    }

    /// <summary>
    /// 拒绝本次重建：不扫描、不合并、不动注册表与脚本运行时（保持进行中 Run 的现场），
    /// 只把拒绝原因写成跳过条目 + 日志 + 报告，并更新 <see cref="LatestReport"/>。
    /// </summary>
    private ContentLoadReport RecordRejectedRebuild(IReadOnlyList<string> enabledModIds)
    {
        var skipped = new List<ModSkipEntry>();
        if (enabledModIds.Count == 0)
        {
            skipped.Add(new ModSkipEntry(
                RebuildRejectedPlaceholderModId,
                ModSkipReason.RebuildRejected,
                RebuildRejectedDetail));
        }
        else
        {
            foreach (var modId in enabledModIds)
            {
                skipped.Add(new ModSkipEntry(modId, ModSkipReason.RebuildRejected, RebuildRejectedDetail));
            }
        }

        foreach (var skip in skipped)
        {
            _logger.LogSkipped(skip);
        }

        var report = ContentLoadReport.Empty.WithSkippedMods(skipped);
        _notifier.OnModLoadCompleted(report);
        LatestReport = report;
        return report;
    }

    private ContentLoadReport RebuildCore(IReadOnlyList<string> enabledModIds)
    {
        var discovery = _discovery.Scan(_modRootDirectory);
        foreach (var skip in discovery.SkippedMods)
        {
            _logger.LogSkipped(skip);
        }

        var activation = _planner.Plan(discovery.ValidMods, enabledModIds, discovery.SkippedMods);
        var discoverySkippedIds = discovery.SkippedMods
            .Select(static s => s.ModId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var skip in activation.SkippedMods)
        {
            if (discoverySkippedIds.Contains(skip.ModId))
            {
                continue;
            }

            _logger.LogSkipped(skip);
        }

        _translationLoader.ClearRegistered();

        var bundles = new List<ModContentBundle>();
        var loadSkipped = new List<ModSkipEntry>();
        foreach (var entry in activation.OrderedActiveMods)
        {
            try
            {
                bundles.Add(ContentModLoader.Load(entry));
                _translationLoader.TryLoadModTranslations(entry);
            }
            catch (ContentModLoadException ex)
            {
                var skip = new ModSkipEntry(ex.ModId, ModSkipReason.LoadFailed, ex.Message);
                loadSkipped.Add(skip);
                _logger.LogSkipped(skip);
            }
        }

        Registry.Rebuild(bundles, out var registryReport);
        foreach (var conflict in registryReport.IdConflicts)
        {
            _logger.LogConflict(conflict);
        }

        foreach (var validationError in registryReport.RemovedValidationErrors)
        {
            _logger.LogValidationError(validationError);
        }

        _scriptCatalog.Rebuild(activation.OrderedActiveMods);
        _scriptRuntimeResetter.Recreate();

        var scriptLoadErrors = _scriptPrewarmer?.Warm().ToList() ?? [];
        foreach (var scriptError in scriptLoadErrors)
        {
            _logger.LogScriptLoadError(scriptError);
        }

        var allSkipped = activation.SkippedMods
            .Concat(loadSkipped)
            .ToList();
        var finalReport = new ContentLoadReport(
            allSkipped,
            registryReport.IdConflicts,
            registryReport.ValidationErrors,
            scriptLoadErrors,
            registryReport.RemovedValidationErrors);
        _notifier.OnModLoadCompleted(finalReport);
        return finalReport;
    }
}