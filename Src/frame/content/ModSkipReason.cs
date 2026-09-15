namespace KemoCard.Frame.Content;

public enum ModSkipReason
{
    InvalidManifest,
    DuplicateModId,
    MissingRequiredDependency,
    CyclicDependency,
    LoadFailed,

    /// <summary>Run 进行中误调用 <c>ContentModPipeline.Rebuild</c>，整次重建被拒绝（内容 Mod 规格 §5）。</summary>
    RebuildRejected,
}