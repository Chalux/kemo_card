namespace KemoCard.Frame.FeatureKit;

public static class FeatureBootstrap
{
	public static void Run(
		IReadOnlyList<IFeaturePackage> packages,
		FeatureCompositionContext context,
		FeatureManager manager)
	{
		for (var i = 0; i < packages.Count; i++)
		{
			context.BeginInstallIndex(i);
			try
			{
				packages[i].Install(context);
			}
			finally
			{
				context.EndInstallIndex();
			}
		}
		ValidateInstallationComplete();
		manager.Initialize();
	}

	public static void Shutdown(FeatureManager manager, FeatureCompositionContext context)
	{
		manager.ShutdownPackages(context);
		context.ShutdownInternalBuses();
		context.ClearGlobalSubscriptions();
	}

	private static void ValidateInstallationComplete()
	{
	}
}
