using Godot;
using KemoCard.Frame.FeatureKit;
using System;
using System.Collections.Generic;

namespace MainRoot;

public partial class MainRoot : Control
{
	public override void _Ready()
	{
		BootstrapFeatures();
	}

	private static void BootstrapFeatures()
	{
		var globalBus = new GlobalEventBus();
		IReadOnlyList<IFeaturePackage> packages = Array.Empty<IFeaturePackage>();
		var manager = new FeatureManager(packages);
		var context = new FeatureCompositionContext(globalBus, manager, packages);
		FeatureBootstrap.Run(packages, context, manager);
		GD.Print("功能框架已启动（当前无功能包）。");
	}
}
