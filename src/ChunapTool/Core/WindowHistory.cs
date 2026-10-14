namespace ChunapTool.Core;

/// <summary>選択したウィンドウの状態を、最大100操作まで戻せる履歴。</summary>
public sealed class WindowHistory
{
    public const int MaximumCount = 100;
    private readonly List<WindowState> _undo = [];
    private WindowIdentity? _target;

    public int Count => _undo.Count;
    public WindowState? Current { get; private set; }

    public void Select(WindowState? state)
    {
        if (state is not null && _target == state.Target)
        {
            Observe(state);
            return;
        }

        _undo.Clear();
        _target = state?.Target;
        Current = state is { IsMinimized: false } ? state : null;
    }

    public void Observe(WindowState state, bool isInteracting = false)
    {
        if (_target != state.Target)
        {
            Select(state);
            return;
        }

        // 最小化時の不可視座標をUndoへ混ぜず、復帰するまで直前の状態を維持する。
        if (state.IsMinimized)
        {
            return;
        }

        if (Current is null)
        {
            Current = state;
            return;
        }

        if (Equivalent(Current, state)
            || (isInteracting && Current.IsMaximized == state.IsMaximized))
        {
            return;
        }

        if (_undo.Count == MaximumCount)
        {
            _undo.RemoveAt(0);
        }

        _undo.Add(Current);
        Current = state;
    }

    // 復元がOS側で失敗しても履歴を消さないため、取得と確定を分離する。
    public bool TryPeekUndo(out WindowState? state)
    {
        state = _undo.Count == 0 ? null : _undo[^1];
        return state is not null;
    }

    public void AcceptUndo(WindowState actual)
    {
        if (_target != actual.Target)
        {
            Select(actual);
            return;
        }

        if (actual.IsMinimized)
        {
            return;
        }

        if (_undo.Count > 0)
        {
            _undo.RemoveAt(_undo.Count - 1);
        }

        Current = actual;
    }

    private static bool Equivalent(WindowState left, WindowState right) =>
        left.Bounds == right.Bounds
        && left.IsMaximized == right.IsMaximized
        && left.Placement.NormalBounds == right.Placement.NormalBounds;
}
