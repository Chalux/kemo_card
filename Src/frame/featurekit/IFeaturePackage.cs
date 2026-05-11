namespace KemoCard.Frame.FeatureKit;

public interface IFeaturePackage
{
	void Install(IFeatureCompositionContext ctx);

	void Shutdown(IFeatureCompositionContext ctx);
}
