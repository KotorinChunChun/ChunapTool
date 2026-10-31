using System.Diagnostics;
using ChunapTool.Core;

namespace ChunapTool.Operations;

/// <summary>位置・サイズ・最大化・復元の要求を送り、反映を確認する。GUI・CLI・管理pipeで共有する。</summary>
internal sealed class WindowOperations(IWindowService windows)
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(70);

    // 状態が2回続けて変わらず、完了条件を満たした時点で確定する。期限内に安定しなくても、最後の状態が条件を満たせば確定する。
    internal static async Task<WindowState> WaitForAsync(IWindowService windows, WindowIdentity target,
        Func<WindowState, bool> completed, TimeSpan timeout, Func<bool>? cancelled = null)
    {
        var watch = Stopwatch.StartNew();
        WindowState? actual = null, previous = null;
        var stable = 0;
        while (watch.Elapsed < timeout)
        {
            await Task.Delay(PollInterval);
            if (cancelled?.Invoke() == true) throw new OperationCanceledException();
            actual = windows.GetState(target) ?? throw new InvalidOperationException("対象が終了しました。");
            stable = actual == previous ? stable + 1 : 0;
            previous = actual;
            if (completed(actual) && stable >= 2) return actual;
        }
        if (actual is not null && completed(actual)) return actual;
        throw new TimeoutException("対象が変更に応答しませんでした。権限または対象アプリの制限を確認してください。");
    }

    // 位置・サイズは、要求どおりになったか、対象アプリの制限で別の値に変わった時点で反映とみなす。
    internal static bool BoundsCompleted(WindowState actual, WindowState before, WindowBounds desired) =>
        actual.Bounds == desired || actual.Bounds != before.Bounds;

    internal static bool MaximizeCompleted(WindowState actual, bool maximized) =>
        actual.IsMaximized == maximized && !actual.IsMinimized;

    // 最大化は通常復元位置で、通常表示は見える矩形で比べる。スナップ中は両者が一致しない。
    internal static bool RestoreCompleted(WindowState actual, WindowState target) =>
        actual.IsMaximized == target.IsMaximized && !actual.IsMinimized
        && (target.IsMaximized ? actual.Placement.NormalBounds == target.Placement.NormalBounds : actual.Bounds == target.Bounds);

    internal WindowState GetState(WindowIdentity target) =>
        windows.GetState(target) ?? throw new TargetGoneException();

    /// <summary>指定しなかった値は現在の見える枠から補い、画面外可でなければ作業領域へ収める。</summary>
    internal async Task<OperationResult> SetBoundsAsync(WindowIdentity target, BoundsRequest request,
        bool allowOutside, bool dryRun, TimeSpan timeout)
    {
        var before = GetState(target);
        if (before.IsMaximized || before.IsMinimized)
            throw new InvalidOperationException("最大化・最小化中は位置やサイズを変更できません。先に通常表示へ戻してください。");
        var requested = request.Resolve(before.Bounds);
        var desired = WindowGeometry.Constrain(requested, windows.GetWorkArea(target), allowOutside);
        var warnings = new List<string>();
        if (desired != requested) warnings.Add("作業領域に収めるため、要求した位置・サイズを補正しました。");
        if (dryRun || desired == before.Bounds) return new(before, dryRun ? null : before, desired, warnings);
        windows.ApplyBounds(target, desired);
        var after = await WaitForAsync(windows, target, state => BoundsCompleted(state, before, desired), timeout);
        if (after.Bounds != desired) warnings.Add("対象アプリの制限により、要求と異なる位置・サイズになりました。");
        return new(before, after, desired, warnings);
    }

    internal async Task<OperationResult> SetMaximizedAsync(WindowIdentity target, bool maximized, bool dryRun, TimeSpan timeout)
    {
        var before = GetState(target);
        if (maximized && !before.CanMaximize) throw new InvalidOperationException("このウィンドウは最大化できません。");
        if (dryRun || (before.IsMaximized == maximized && !before.IsMinimized)) return new(before, dryRun ? null : before, null, []);
        windows.SetMaximized(target, maximized);
        var after = await WaitForAsync(windows, target, state => MaximizeCompleted(state, maximized), timeout);
        return new(before, after, null, []);
    }

    internal async Task<OperationResult> RestoreAsync(WindowIdentity target, WindowState saved, bool dryRun, TimeSpan timeout)
    {
        var before = GetState(target);
        // 保存した状態は別の対象から取ったものでもよい。復元先は呼出し側が指定した対象に限る。
        var restore = saved with { Target = target };
        if (dryRun) return new(before, null, restore.Bounds, []);
        windows.Restore(restore);
        var after = await WaitForAsync(windows, target, state => RestoreCompleted(state, restore), timeout);
        return new(before, after, restore.Bounds, []);
    }
}

internal sealed record BoundsRequest(int? X, int? Y, int? Width, int? Height)
{
    internal bool IsEmpty => X is null && Y is null && Width is null && Height is null;
    internal WindowBounds Resolve(WindowBounds current) =>
        new(X ?? current.X, Y ?? current.Y, Width ?? current.Width, Height ?? current.Height);
}

/// <summary>Afterは試行（dry-run）ではnull。Requestedは送った（送るはずだった）見える枠。</summary>
internal sealed record OperationResult(WindowState Before, WindowState? After, WindowBounds? Requested, IReadOnlyList<string> Warnings);

internal sealed class TargetGoneException() : InvalidOperationException("対象ウィンドウは終了したか、別のプロセスに置き換わりました。");
