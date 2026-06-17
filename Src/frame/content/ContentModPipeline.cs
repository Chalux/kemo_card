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

	public ContentLoadReport Rebuild(IReadOnlyList<string> enabledModIds)
	{
		ArgumentNullException.ThrowIfNull(enabledModIds);

		_scriptRuntimeResetter.BeginRebuild();
		try
		{
			return RebuildCore(enabledModIds);
		}
		finally
		{
			_scriptRuntimeResetter.EndRebuild();
		}
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

		foreach (var validationError in registryReport.ValidationErrors)
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
			scriptLoadErrors);
		_notifier.OnModLoadCompleted(finalReport);
		return finalReport;
	}
}
