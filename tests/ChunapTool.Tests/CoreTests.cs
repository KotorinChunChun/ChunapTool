using ChunapTool.Core;

namespace ChunapTool.Tests;

internal static class CoreTests
{
    private static readonly WindowIdentity Target = new((nint)123, 456);

    public static int Run()
    {
        (string Name, Action Execute)[] tests =
        [
            ("画面外許可では座標を維持して最小サイズだけを適用", GeometryAllowOutside),
            ("作業領域内の要求は変更しない", GeometryAlreadyInside),
            ("領域を越える位置とサイズを制限", GeometryOversized),
            ("左上が負座標のモニターへ収める", GeometryNegativeMonitor),
            ("最小サイズ未満の作業領域にも収める", GeometryTinyArea),
            ("無効な作業領域でも正のサイズを返す", GeometryInvalidArea),
            ("int境界でも座標と端点がオーバーフローしない", GeometryIntegerBoundaries),
            ("各境界の組合せで領域内と冪等性を満たす", GeometryInvariants),
            ("初期状態にはUndo履歴がない", HistoryEmpty),
            ("初回最大化状態をそのまま保持", HistoryInitiallyMaximized),
            ("通常の移動をUndoできる", HistoryNormalMove),
            ("同じ位置と復元位置の重複を抑制", HistoryDuplicates),
            ("見える矩形が同じでも通常復元位置の変更を記録", HistoryNormalPlacementChange),
            ("最大化と通常状態の往復を保持", HistoryMaximizeRestore),
            ("最小化の不可視位置を記録しない", HistoryMinimized),
            ("最小化中に初回選択しても復帰位置を基準にする", HistoryInitiallyMinimized),
            ("ドラッグ中は記録せず終了時に1操作", HistoryDrag),
            ("ドラッグ中でも最大化の切替は即座に記録", HistoryMaximizeDuringDrag),
            ("100操作を超える履歴は古いものから除外", HistoryLimit),
            ("同じハンドルでもPIDが変われば履歴を破棄", HistoryIdentityChange),
            ("同じ対象の再選択では履歴を保つ", HistoryReselect),
            ("選択解除では履歴と基準を消去", HistoryDeselect),
            ("Undo取得だけでは履歴を消費しない", HistoryPeekWithoutCommit),
            ("Undo成功後は実際の復元位置を基準にする", HistoryAcceptActual),
            ("最小化した別対象への切替も旧履歴を消去", HistorySwitchToMinimized),
            ("別対象のUndo結果は旧履歴と混在させない", HistoryAcceptDifferentTarget),
            ("最小化状態ではUndoの成功を確定しない", HistoryRejectMinimizedUndo),
        ];

        int failures = 0;
        foreach (var test in tests)
        {
            try
            {
                test.Execute();
                Console.WriteLine($"成功: {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"失敗: {test.Name}\n{exception}");
            }
        }

        Console.WriteLine($"単体検証: {tests.Length - failures}/{tests.Length} 成功");
        return failures == 0 ? 0 : 1;
    }

    private static void GeometryAllowOutside()
    {
        var requested = new WindowBounds(int.MinValue, int.MaxValue, -10, 2);
        Equal(new WindowBounds(int.MinValue, int.MaxValue, 32, 32),
            WindowGeometry.Constrain(requested, new WindowBounds(0, 0, 1920, 1080), true));
        Equal(new WindowBounds(-500, 1400, int.MaxValue, 400),
            WindowGeometry.Constrain(new WindowBounds(-500, 1400, int.MaxValue, 400), default, true));
    }

    private static void GeometryAlreadyInside() =>
        Equal(new WindowBounds(100, 200, 400, 300), WindowGeometry.Constrain(
            new WindowBounds(100, 200, 400, 300), new WindowBounds(0, 40, 1920, 1000), false));

    private static void GeometryOversized()
    {
        Equal(new WindowBounds(0, 40, 1920, 1000), WindowGeometry.Constrain(
            new WindowBounds(-100, -200, 4000, 2000), new WindowBounds(0, 40, 1920, 1000), false));
        Equal(new WindowBounds(1520, 740, 400, 300), WindowGeometry.Constrain(
            new WindowBounds(1910, 1030, 400, 300), new WindowBounds(0, 40, 1920, 1000), false));
    }

    private static void GeometryNegativeMonitor() =>
        Equal(new WindowBounds(-400, -300, 400, 300), WindowGeometry.Constrain(
            new WindowBounds(100, 100, 400, 300), new WindowBounds(-1920, -1080, 1920, 1080), false));

    private static void GeometryTinyArea() =>
        Equal(new WindowBounds(-5, 7, 12, 9), WindowGeometry.Constrain(
            new WindowBounds(500, 500, 1, 1), new WindowBounds(-5, 7, 12, 9), false));

    private static void GeometryInvalidArea() =>
        Equal(new WindowBounds(20, -30, 1, 1), WindowGeometry.Constrain(
            new WindowBounds(100, 100, -1, -1), new WindowBounds(20, -30, 0, -100), false));

    private static void GeometryIntegerBoundaries()
    {
        Equal(new WindowBounds(int.MaxValue - 10, int.MinValue + 10, 32, 32),
            WindowGeometry.Constrain(new WindowBounds(int.MinValue, int.MinValue, 1, 1),
                new WindowBounds(int.MaxValue - 10, int.MinValue + 10, int.MaxValue, 100), false));
        Equal(new WindowBounds(-33, -33, 32, 32),
            WindowGeometry.Constrain(new WindowBounds(int.MaxValue, int.MaxValue, 1, 1),
                new WindowBounds(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), false));
    }

    private static void GeometryInvariants()
    {
        int[] positions = [int.MinValue, -1920, -1, 0, 1, 1920, int.MaxValue];
        int[] sizes = [int.MinValue, -1, 0, 1, 31, 32, 1920, int.MaxValue];
        foreach (int start in positions)
        foreach (int requestedPosition in positions)
        foreach (int areaSize in sizes.Where(value => value > 0))
        foreach (int requestedSize in sizes)
        {
            var area = new WindowBounds(start, start, areaSize, areaSize);
            var result = WindowGeometry.Constrain(
                new WindowBounds(requestedPosition, requestedPosition, requestedSize, requestedSize), area, false);
            True(result.Width > 0 && result.Height > 0, "サイズは正の値");
            True(result.X >= area.X && result.Y >= area.Y, "左端と上端は領域内");
            True((long)result.X + result.Width <= (long)area.X + area.Width, "右端は領域内");
            True((long)result.Y + result.Height <= (long)area.Y + area.Height, "下端は領域内");
            Equal(result, WindowGeometry.Constrain(result, area, false));
        }
    }

    private static void HistoryEmpty()
    {
        var history = new WindowHistory();
        Equal(0, history.Count);
        Equal<WindowState?>(null, history.Current);
        True(!history.TryPeekUndo(out var previous), "初期状態のUndoは不可");
        Equal<WindowState?>(null, previous);
    }

    private static void HistoryInitiallyMaximized()
    {
        var initial = State(0, 3);
        var history = new WindowHistory();
        history.Select(initial);
        Equal(initial, history.Current);
        Equal(0, history.Count);
        history.Observe(initial);
        Equal(0, history.Count);
    }

    private static void HistoryNormalMove()
    {
        var history = Start();
        history.Observe(State(20));
        Equal(1, history.Count);
        Equal(State(20), history.Current);
        Equal(State(0), Peek(history));
    }

    private static void HistoryDuplicates()
    {
        var initial = State(0);
        var history = Start(initial);
        history.Observe(initial with
        {
            Placement = initial.Placement with
            {
                Flags = 2,
                ShowCommand = 9,
                MinPosition = new WindowPoint(10, 20),
                MaxPosition = new WindowPoint(20, 30),
            },
            CanMaximize = false,
        });
        Equal(0, history.Count);
    }

    private static void HistoryNormalPlacementChange()
    {
        var initial = State(0, 3);
        var history = Start(initial);
        var changed = initial with { Placement = initial.Placement with { NormalBounds = new WindowBounds(10, 20, 300, 400) } };
        history.Observe(changed);
        Equal(1, history.Count);
        Equal(initial, Peek(history));
        Equal(changed, history.Current);
    }

    private static void HistoryMaximizeRestore()
    {
        var normal = State(0);
        var maximized = State(0, 3);
        var history = Start(normal);
        history.Observe(maximized);
        history.Observe(normal);
        Equal(2, history.Count);
        Equal(maximized, Peek(history));
        history.AcceptUndo(maximized);
        Equal(normal, Peek(history));
    }

    private static void HistoryMinimized()
    {
        var history = Start();
        foreach (uint command in new uint[] { 2, 6, 7, 11 })
        {
            history.Observe(State(-32000, command));
            Equal(0, history.Count);
            Equal(State(0), history.Current);
        }

        history.Observe(State(0));
        Equal(0, history.Count);
        history.Observe(State(25));
        Equal(State(0), Peek(history));
    }

    private static void HistoryInitiallyMinimized()
    {
        var history = Start(State(-32000, 2));
        Equal<WindowState?>(null, history.Current);
        history.Observe(State(75));
        Equal(State(75), history.Current);
        Equal(0, history.Count);
    }

    private static void HistoryDrag()
    {
        var history = Start();
        history.Observe(State(10), isInteracting: true);
        history.Observe(State(20), isInteracting: true);
        Equal(0, history.Count);
        Equal(State(0), history.Current);
        history.Observe(State(30));
        Equal(1, history.Count);
        Equal(State(0), Peek(history));
    }

    private static void HistoryMaximizeDuringDrag()
    {
        var history = Start();
        history.Observe(State(10), isInteracting: true);
        history.Observe(State(0, 3), isInteracting: true);
        Equal(1, history.Count);
        Equal(State(0), Peek(history));
        Equal(State(0, 3), history.Current);
        history.Observe(State(0, 3));
        Equal(1, history.Count);
        history.Observe(State(20), isInteracting: true);
        Equal(2, history.Count);
        Equal(State(0, 3), Peek(history));
    }

    private static void HistoryLimit()
    {
        var history = Start();
        for (int index = 1; index <= 105; index++)
        {
            history.Observe(State(index));
        }

        Equal(100, history.Count);
        for (int expected = 104; expected >= 5; expected--)
        {
            var previous = Peek(history);
            Equal(State(expected), previous);
            history.AcceptUndo(previous);
        }

        Equal(0, history.Count);
        True(!history.TryPeekUndo(out _), "保持上限を越えた履歴は戻せない");
    }

    private static void HistoryIdentityChange()
    {
        var history = Start();
        history.Observe(State(10));
        var replacement = State(20) with { Target = new WindowIdentity(Target.Handle, Target.ProcessId + 1) };
        history.Observe(replacement);
        Equal(0, history.Count);
        Equal(replacement, history.Current);
    }

    private static void HistoryReselect()
    {
        var history = Start();
        history.Observe(State(10));
        history.Select(State(10));
        Equal(1, history.Count);
        history.Select(State(20));
        Equal(2, history.Count);
        Equal(State(10), Peek(history));
    }

    private static void HistoryDeselect()
    {
        var history = Start();
        history.Observe(State(10));
        history.Select(null);
        Equal(0, history.Count);
        Equal<WindowState?>(null, history.Current);
        history.Select(State(20));
        Equal(0, history.Count);
        Equal(State(20), history.Current);
    }

    private static void HistoryPeekWithoutCommit()
    {
        var history = Start();
        history.Observe(State(10));
        Equal(State(0), Peek(history));
        Equal(State(0), Peek(history));
        Equal(1, history.Count);
        Equal(State(10), history.Current);
    }

    private static void HistoryAcceptActual()
    {
        var history = Start();
        history.Observe(State(10));
        history.Observe(State(20));
        Equal(State(10), Peek(history));
        var actual = State(11);
        history.AcceptUndo(actual);
        Equal(1, history.Count);
        Equal(actual, history.Current);
        history.Observe(actual);
        Equal(1, history.Count);
        Equal(State(0), Peek(history));
    }

    private static void HistorySwitchToMinimized()
    {
        var history = Start();
        history.Observe(State(10));
        var minimized = State(-32000, 2) with { Target = new WindowIdentity((nint)999, 987) };
        history.Observe(minimized);
        Equal(0, history.Count);
        Equal<WindowState?>(null, history.Current);
        history.Observe(minimized with { Placement = minimized.Placement with { ShowCommand = 3 } });
        Equal(0, history.Count);
        True(history.Current?.IsMaximized == true, "復帰後の最大化状態が初期状態");
    }

    private static void HistoryAcceptDifferentTarget()
    {
        var history = Start();
        history.Observe(State(10));
        var actual = State(100) with { Target = new WindowIdentity((nint)999, 987) };
        history.AcceptUndo(actual);
        Equal(0, history.Count);
        Equal(actual, history.Current);
    }

    private static void HistoryRejectMinimizedUndo()
    {
        var history = Start();
        history.Observe(State(10));
        history.AcceptUndo(State(-32000, 2));
        Equal(1, history.Count);
        Equal(State(10), history.Current);
    }

    private static WindowHistory Start(WindowState? initial = null)
    {
        var history = new WindowHistory();
        history.Select(initial ?? State(0));
        return history;
    }

    private static WindowState State(int x, uint showCommand = 1)
    {
        var bounds = new WindowBounds(x, 50, 640, 480);
        return new WindowState(Target, bounds,
            new WindowPlacement(0, showCommand, new WindowPoint(-1, -1), new WindowPoint(-1, -1), bounds), true);
    }

    private static WindowState Peek(WindowHistory history)
    {
        True(history.TryPeekUndo(out var previous), "Undo候補が存在する");
        return previous ?? throw new InvalidOperationException("Undo候補がnullです。");
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"期待値: {expected}\n実際値: {actual}");
        }
    }
}
