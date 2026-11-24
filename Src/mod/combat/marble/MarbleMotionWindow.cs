namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 每颗球的运动观察窗口（固定物理步）：同时记录球心轨迹与实际行进距离。
/// 困球判定要求「观察窗口内净位移小 <b>且</b> 路径短」——狭缝高速往返的净位移也小，
/// 但路径很长，因此不会误判；缓慢但持续的运动则因净位移超出容差而不算困球。
/// </summary>
internal sealed class MarbleMotionWindow
{
    private readonly MarbleVec[] _positions;
    private readonly float[] _distances;
    private int _count;
    private int _head;

    public MarbleMotionWindow(int capacity)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _positions = new MarbleVec[capacity];
        _distances = new float[capacity];
    }

    /// <summary>记录一步：<paramref name="position"/> 为该步结束后的球心，<paramref name="distance"/> 为该步实际行进距离。</summary>
    public void Push(MarbleVec position, float distance)
    {
        _positions[_head] = position;
        _distances[_head] = distance;
        _head = (_head + 1) % _positions.Length;
        if (_count < _positions.Length)
            _count++;
    }

    /// <summary>
    /// 最近 <paramref name="samples"/> 步是否几乎静止。
    /// 需要窗口外还有一个历史位置（<c>count &gt; samples</c>）才能计算净位移；
    /// 因此刚发射的球至少观察一个完整窗口后才会被判定。
    /// </summary>
    public bool IsNearlyStill(int samples, float displacementTolerance, float pathTolerance)
    {
        if (samples <= 0 || samples >= _positions.Length || _count <= samples)
            return false;

        var capacity = _positions.Length;
        var newestIndex = (_head - 1 + capacity) % capacity;
        var beforeWindowIndex = (_head - samples - 1 + capacity) % capacity;
        var displacement = MarbleVec.Distance(_positions[newestIndex], _positions[beforeWindowIndex]);

        var path = 0f;
        for (var i = 0; i < samples; i++)
            path += _distances[(_head - samples + i + capacity) % capacity];

        return displacement <= displacementTolerance && path <= pathTolerance;
    }
}
