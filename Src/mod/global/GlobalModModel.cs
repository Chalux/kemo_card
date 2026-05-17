using KemoCard.Frame.Mvc;
using KemoCard.Mod.Global.Save;

namespace KemoCard.Mod.Global;

public sealed class GlobalModModel : FeatureModelBase
{
	public GlobalSaveDto Current { get; internal set; } = GlobalSaveDto.CreateDefault();
}
