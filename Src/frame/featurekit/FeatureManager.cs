using System.Collections.Concurrent;

namespace KemoCard.Frame.FeatureKit;

public interface IFeatureManagerReadOnly
{
	TFacade? TryGetFacade<TFacade>() where TFacade : class;
}

public sealed class FeatureManager : IFeatureManagerReadOnly
{
	private readonly IReadOnlyList<IFeaturePackage> _packages;
	private readonly ConcurrentDictionary<Type, object> _facades = new();

	public FeatureManager(IReadOnlyList<IFeaturePackage> packages)
	{
		_packages = packages;
	}

	public void RegisterFacade<TFacade>(TFacade facade) where TFacade : class
	{
		var t = typeof(TFacade);
		_facades[t] = facade;
	}

	public TFacade? TryGetFacade<TFacade>() where TFacade : class
	{
		if (_facades.TryGetValue(typeof(TFacade), out var obj) && obj is TFacade f)
		{
			return f;
		}
		return null;
	}

	/// <summary>仅调试或 frame 内编排使用；业务代码勿依赖。</summary>
	public TPackage? GetPackage<TPackage>()
		where TPackage : class, IFeaturePackage
	{
		foreach (var p in _packages)
		{
			if (p is TPackage match)
			{
				return match;
			}
		}
		return null;
	}

	public void Initialize()
	{
	}

	public void ShutdownPackages(IFeatureCompositionContext ctx)
	{
		for (var i = _packages.Count - 1; i >= 0; i--)
		{
			_packages[i].Shutdown(ctx);
		}
	}
}
