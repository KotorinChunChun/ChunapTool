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

    /// <summary>
    /// 要求を、送る見える枠へ換算する。指定しなかった値は現在の値（要求の基準）から補い、比率固定では片方の辺から他方を求める。
    /// 画面外可でなければ作業領域へ収める。比率固定では、収めるために縮める場合も比率を保つ。GUIとCLIで共有する。
    /// </summary>
    internal static BoundsPlan Plan(WindowState before, BoundsRequest request, WindowBounds workArea, bool allowOutside)
    {
        var basis = request.Basis;
        if (basis == BoundsBasis.Client && before.ClientBounds is null)
            throw new InvalidOperationException("対象のクライアント領域を取得できないため、中身の基準で指定できません。");
        var current = BoundsBases.Read(before, basis);
        var resolved = request.Resolve(current);
        if (request.Aspect is { } ratio && (request.Width is null || request.Height is null))
        {
            var (width, height) = AspectRatio.Fit(request.Width, request.Height, ratio, current.Width);
            resolved = resolved with { Width = width, Height = height };
        }
        var frame = BoundsBases.ToFrame(resolved, before, basis);
        var warnings = new List<string>();
        if (request.Aspect is { } keep && !allowOutside)
        {
            // 基準と見える枠の差を除いた、作業領域に収まる最大の大きさ。
            var (width, height) = AspectRatio.Shrink(resolved.Width, resolved.Height, keep,
                workArea.Width - (frame.Width - resolved.Width), workArea.Height - (frame.Height - resolved.Height));
            if ((width, height) != (resolved.Width, resolved.Height))
            {
                resolved = resolved with { Width = width, Height = height };
                frame = BoundsBases.ToFrame(resolved, before, basis);
                warnings.Add("作業領域に収めるため、比率を保ったまま縮めました。");
            }
        }
        var desired = WindowGeometry.Constrain(frame, workArea, allowOutside);
        if (desired != frame && warnings.Count == 0) warnings.Add("作業領域に収めるため、要求した位置・サイズを補正しました。");
        else if (desired != frame) warnings.Add("作業領域に収めるため、位置を補正しました。");
        return new(desired, BoundsBases.FromFrame(desired, before, basis), warnings);
    }

    internal async Task<OperationResult> SetBoundsAsync(WindowIdentity target, BoundsRequest request,
        bool allowOutside, bool dryRun, TimeSpan timeout)
    {
        var before = GetState(target);
        if (before.IsMaximized || before.IsMinimized)
            throw new InvalidOperationException("最大化・最小化中は位置やサイズを変更できません。先に通常表示へ戻してください。");
        var plan = Plan(before, request, windows.GetWorkArea(target), allowOutside);
        var (desired, warnings) = (plan.Frame, plan.Warnings.ToList());
        OperationResult Result(WindowState? after) => new(before, after, desired, warnings) { Basis = request.Basis, RequestedInBasis = plan.InBasis };
        if (dryRun || desired == before.Bounds) return Result(dryRun ? null : before);
        windows.ApplyBounds(target, desired);
        var after = await WaitForAsync(windows, target, state => BoundsCompleted(state, before, desired), timeout);
        if (after.Bounds != desired) warnings.Add("対象アプリの制限により、要求と異なる位置・サイズになりました。");
        return Result(after);
    }

    /// <summary>プリセットは位置を変えず、プリセットの基準で幅・高さだけを変える。</summary>
    internal Task<OperationResult> ApplyPresetAsync(WindowIdentity target, Preset preset, bool allowOutside, bool dryRun, TimeSpan timeout) =>
        SetBoundsAsync(target, new BoundsRequest(null, null, preset.Width, preset.Height) { Basis = preset.Basis }, allowOutside, dryRun, timeout);

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
    internal BoundsBasis Basis { get; init; } = BoundsBasis.Frame;
    // 固定する縦横比（幅÷高さ）。nullなら固定しない。
    internal double? Aspect { get; init; }
    internal bool IsEmpty => X is null && Y is null && Width is null && Height is null;
    internal WindowBounds Resolve(WindowBounds current) =>
        new(X ?? current.X, Y ?? current.Y, Width ?? current.Width, Height ?? current.Height);
}

/// <summary>Frameは送る見える枠、InBasisは同じ矩形を要求の基準で表したもの。</summary>
internal sealed record BoundsPlan(WindowBounds Frame, WindowBounds InBasis, IReadOnlyList<string> Warnings);

/// <summary>Afterは試行（dry-run）ではnull。Requestedは送った（送るはずだった）見える枠。</summary>
internal sealed record OperationResult(WindowState Before, WindowState? After, WindowBounds? Requested, IReadOnlyList<string> Warnings)
{
    internal BoundsBasis Basis { get; init; } = BoundsBasis.Frame;
    internal WindowBounds? RequestedInBasis { get; init; }
}

internal sealed class TargetGoneException() : InvalidOperationException("対象ウィンドウは終了したか、別のプロセスに置き換わりました。");
