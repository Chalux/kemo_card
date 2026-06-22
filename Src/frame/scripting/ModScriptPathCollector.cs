using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Scripting;

public static class ModScriptPathCollector
{
	public static IReadOnlyList<(string ModId, string ScriptPath)> Collect(GameDefinitionRegistry registry)
	{
		var store = registry.Store;
		var paths = new List<(string ModId, string ScriptPath)>();

		foreach (var (id, effect) in store.Effects)
		{
			if (effect.Kind == EEffectKind.ExecuteScript && !string.IsNullOrWhiteSpace(effect.ScriptPath)
				&& registry.TryGetOwnerModId(EContentCategory.Effect, id, out var modId))
			{
				paths.Add((modId, effect.ScriptPath));
			}
		}

		foreach (var (id, eventDef) in store.Events)
		{
			if (eventDef.EventKind == EEventKind.Script && !string.IsNullOrWhiteSpace(eventDef.ScriptPath)
				&& registry.TryGetOwnerModId(EContentCategory.Event, id, out var modId))
			{
				paths.Add((modId, eventDef.ScriptPath));
			}
		}

		foreach (var (id, battle) in store.Battles)
		{
			if (registry.TryGetOwnerModId(EContentCategory.Battle, id, out var modId))
			{
				if (!string.IsNullOrWhiteSpace(battle.ScriptPath))
				{
					paths.Add((modId, battle.ScriptPath));
				}

				foreach (var wave in battle.Waves)
				{
					if (!string.IsNullOrWhiteSpace(wave.WaveScriptPath))
					{
						paths.Add((modId, wave.WaveScriptPath));
					}
				}
			}
		}

		foreach (var (id, enemy) in store.Enemies)
		{
			if (!string.IsNullOrWhiteSpace(enemy.ScriptPath)
				&& registry.TryGetOwnerModId(EContentCategory.Enemy, id, out var modId))
			{
				paths.Add((modId, enemy.ScriptPath));
			}
		}

		foreach (var (id, skill) in store.Skills)
		{
			if (!string.IsNullOrWhiteSpace(skill.ScriptPath)
				&& registry.TryGetOwnerModId(EContentCategory.Skill, id, out var modId))
			{
				paths.Add((modId, skill.ScriptPath));
			}
		}

		foreach (var (id, buff) in store.Buffs)
		{
			if (!string.IsNullOrWhiteSpace(buff.ScriptPath)
				&& registry.TryGetOwnerModId(EContentCategory.Buff, id, out var modId))
			{
				paths.Add((modId, buff.ScriptPath));
			}
		}

		foreach (var (id, action) in store.SkillActions)
		{
			if (action.Kind == ESkillActionKind.ExecuteScript &&
				!string.IsNullOrWhiteSpace(action.ScriptPath) &&
				registry.TryGetOwnerModId(EContentCategory.SkillAction, id, out var modId))
			{
				paths.Add((modId, action.ScriptPath));
			}
		}

		return paths
			.Distinct()
			.OrderBy(static pair => pair.ModId, StringComparer.Ordinal)
			.ThenBy(static pair => pair.ScriptPath, StringComparer.Ordinal)
			.ToList();
	}
}
