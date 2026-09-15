using System.Text;

namespace KemoCard.Frame.Scripting;

public sealed class HostRng
{
    private readonly Random _random;

    public int RunSeed { get; }

    public HostRng(int runSeed, string streamKey)
    {
        ArgumentNullException.ThrowIfNull(streamKey);

        RunSeed = runSeed;
        _random = new Random(DeriveSeed(runSeed, streamKey));
    }

    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    /// <summary>
    /// 稳定种子派生：FNV-1a 混合 runSeed 与 streamKey 的 UTF-8 字节。
    /// </summary>
    /// <remarks>
    /// 不得改用 <c>HashCode.Combine</c> / <c>string.GetHashCode()</c>：.NET Core 的字符串哈希
    /// 按进程随机化，会让同一 RunSeed 在重启进程后产生不同随机序列，破坏
    /// "RunSeed + StreamKey 可复现"（jsenv-mod-scripting 规格 §3）与存档续玩语义。
    /// 本实现只依赖算术，跨进程、跨 .NET 版本结果一致。
    /// </remarks>
    internal static int DeriveSeed(int runSeed, string streamKey)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;

        for (var i = 0; i < sizeof(int); i++)
        {
            hash = unchecked((hash ^ (byte)(runSeed >> (i * 8))) * prime);
        }

        var bytes = Encoding.UTF8.GetBytes(streamKey);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash = unchecked((hash ^ bytes[i]) * prime);
        }

        return unchecked((int)hash);
    }
}