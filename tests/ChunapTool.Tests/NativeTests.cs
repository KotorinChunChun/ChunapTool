using System.ComponentModel;
using System.Diagnostics;
using ChunapTool.Core;
using ChunapTool.Native;

namespace ChunapTool.Tests;

/// <summary>この検証が起動した専用プロセスだけを操作するWindows実機検証。</summary>
internal static class NativeTests
{
    public static int Run()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Console.Error.WriteLine("実機検証はSTAスレッドで起動してください。");
            return 1;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var service = new NativeWindowService();
        Process? fixture = null;
        int successes = 0, failures = 0;
        using var host = new BrandedForm
        {
            Text = WindowTitle.Create("プレビュー実機検証"),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(32, 80),
            ClientSize = new Size(420, 310),
            BackColor = Color.Black,
            ShowInTaskbar = true
        };

        try
        {
            fixture = StartFixture();
            uint fixtureId = (uint)fixture.Id;
            WindowEntry? entry = null;
            WaitUntil(() =>
            {
                if (fixture.HasExited)
                    throw new InvalidOperationException("動作確認用プロセスが起動直後に終了しました。");
                entry = service.Enumerate("動作確認用").SingleOrDefault(candidate => candidate.Identity.ProcessId == fixtureId);
                return entry is not null;
            }, "動作確認用ウィンドウの起動");
            WindowIdentity target = entry!.Identity;
            Require(target.ProcessId == fixtureId && target.ProcessId != (uint)Environment.ProcessId,
                "この検証が起動した別プロセス以外を操作しようとしました。");
            WindowState original = ReadState(service, target);
            WindowBounds work = service.GetWorkArea(target);
            host.Show();
            PumpFor(120);

            void Case(string name, Action body)
            {
                try
                {
                    body();
                    successes++;
                    Console.WriteLine($"成功: {name}");
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine($"失敗: {name}\n{error}");
                }
            }

            void RestoreOriginal()
            {
                service.Restore(original);
                WaitForState(service, target, state => !state.IsMinimized && !state.IsMaximized
                    && state.Placement.NormalBounds == original.Placement.NormalBounds,
                    "動作確認用ウィンドウの初期位置への復元");
            }

            Case("列挙・タイトル検索・実行ファイル名検索・自プロセス除外", () =>
            {
                var all = service.Enumerate("");
                Require(all.Any(candidate => candidate.Identity == target), "専用ウィンドウを列挙できません。");
                Require(all.All(candidate => candidate.Identity.ProcessId != (uint)Environment.ProcessId),
                    "自プロセスが候補に含まれています。");
                Require(service.Enumerate("CHUNAPTOOL").Any(candidate => candidate.Identity == target),
                    "大文字小文字を無視したタイトル検索ができません。");
                Require(service.Enumerate(entry.ProcessName.ToUpperInvariant()).Any(candidate => candidate.Identity == target),
                    "実行ファイル名の検索ができません。");
                var expected = all.OrderBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.Identity.ProcessId).ThenBy(candidate => candidate.Identity.Handle);
                Require(all.SequenceEqual(expected), "候補の並び順が不正です。");
            });

            Case("DWMの見える矩形を基準に移動・サイズ変更", () =>
            {
                RestoreOriginal();
                var requested = WindowGeometry.Constrain(new WindowBounds(work.X + 60, work.Y + 60,
                    original.Bounds.Width + 24, original.Bounds.Height + 20), work, false);
                service.ApplyBounds(target, requested);
                var actual = WaitForState(service, target, state => state.Bounds == requested,
                    "指定した見える矩形への移動・サイズ変更");
                Require(actual.Bounds == requested, $"見える矩形が一致しません。要求={requested}, 実測={actual.Bounds}");
            });

            Case("最大化してから解除すると直前の通常矩形へ戻る", () =>
            {
                RestoreOriginal();
                var normal = ReadState(service, target);
                Require(normal.CanMaximize, "専用ウィンドウの最大化ボタンが無効です。");
                service.SetMaximized(target, true);
                WaitForState(service, target, state => state.IsMaximized, "最大化");
                Throws<InvalidOperationException>(() => service.ApplyBounds(target, normal.Bounds));
                service.SetMaximized(target, false);
                WaitForState(service, target, state => !state.IsMaximized && !state.IsMinimized
                    && state.Bounds == normal.Bounds, "最大化解除後の通常矩形");
            });

            Case("保存したWINDOWPLACEMENTで通常位置を復元", () =>
            {
                RestoreOriginal();
                var saved = ReadState(service, target);
                var moved = WindowGeometry.Constrain(saved.Bounds with { X = work.X + 100, Y = work.Y + 100 }, work, false);
                service.ApplyBounds(target, moved);
                WaitForState(service, target, state => state.Bounds == moved, "復元前の移動");
                service.Restore(saved);
                WaitForState(service, target, state => state.Placement.NormalBounds == saved.Placement.NormalBounds
                    && state.Bounds == saved.Bounds && !state.IsMaximized, "保存した通常位置の復元");
            });

            Case("最大化状態の復元と、その後の通常位置の維持", () =>
            {
                RestoreOriginal();
                service.SetMaximized(target, true);
                var saved = WaitForState(service, target, state => state.IsMaximized, "保存用の最大化");
                service.SetMaximized(target, false);
                WaitForState(service, target, state => !state.IsMaximized, "一時的な最大化解除");
                service.Restore(saved);
                WaitForState(service, target, state => state.IsMaximized
                    && state.Placement.NormalBounds == saved.Placement.NormalBounds, "保存した最大化状態の復元");
                service.SetMaximized(target, false);
                WaitForState(service, target, state => !state.IsMaximized && state.Bounds == original.Bounds,
                    "最大化履歴の復元後も通常位置が保たれること");
            });

            Case("最小化中の位置変更を拒否し、保存状態で復元", () =>
            {
                RestoreOriginal();
                Require(NativeMethods.ShowWindowAsync(target.Handle, 6), "最小化要求に失敗しました。");
                WaitForState(service, target, state => state.IsMinimized, "最小化");
                Throws<InvalidOperationException>(() => service.ApplyBounds(target, original.Bounds));
                RestoreOriginal();
            });

            Case("PIDが一致しないハンドルを拒否し、実対象を変更しない", () =>
            {
                RestoreOriginal();
                var before = ReadState(service, target);
                var mismatch = target with { ProcessId = 0 };
                Require(service.GetState(mismatch) is null, "異なるPIDの状態を返しました。");
                Throws<Win32Exception>(() => service.ApplyBounds(mismatch, before.Bounds));
                Throws<Win32Exception>(() => service.SetMaximized(mismatch, true));
                Throws<Win32Exception>(() => service.GetWorkArea(mismatch));
                Throws<Win32Exception>(() => service.Restore(before with { Target = mismatch }));
                Require(ReadState(service, target) == before, "拒否した操作で実対象が変更されました。");
            });

            Case("DWMプレビューの登録・表示更新・解除・再登録・多重Dispose", () =>
            {
                RestoreOriginal();
                var preview = new DwmPreview();
                try
                {
                    preview.Attach(host.Handle, target.Handle);
                    Require(preview.IsAttached, "プレビューが登録されていません。");
                    preview.Update(new Rectangle(12, 12, 396, 286));
                    PumpFor(350);
                    preview.Update(new Rectangle(24, 20, 300, 180));
                    PumpFor(200);
                    preview.Update(Rectangle.Empty, false);
                    preview.Clear();
                    preview.Clear();
                    Require(!preview.IsAttached, "解除後もプレビューが残っています。");
                    preview.Attach(host.Handle, target.Handle);
                    preview.Update(host.ClientRectangle);
                    PumpFor(250);
                    preview.Dispose();
                    preview.Dispose();
                    Require(!preview.IsAttached, "Dispose後もプレビューが残っています。");
                    Throws<ObjectDisposedException>(() => preview.Update(host.ClientRectangle));
                }
                finally { preview.Dispose(); }
            });

            Case("専用対象の終了後は状態なしとなり、プレビューを安全に解除", () =>
            {
                using var preview = new DwmPreview();
                preview.Attach(host.Handle, target.Handle);
                preview.Update(host.ClientRectangle);
                fixture.Refresh();
                Require(fixture.CloseMainWindow(), "専用ウィンドウへ終了を要求できませんでした。");
                WaitUntil(() => fixture.HasExited, "専用プロセスの正常終了");
                Require(service.GetState(target) is null, "終了した対象の状態が残っています。");
                Throws<Win32Exception>(() => service.ApplyBounds(target, original.Bounds));
                Throws<Win32Exception>(() => preview.Update(host.ClientRectangle));
                preview.Clear();
                preview.Clear();
            });
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine($"実機検証の準備または進行に失敗しました。\n{error}");
        }
        finally
        {
            host.Close();
            if (fixture is not null)
            {
                try
                {
                    if (!fixture.HasExited)
                    {
                        fixture.Refresh();
                        fixture.CloseMainWindow();
                        WaitUntil(() => fixture.HasExited, "後始末時の専用プロセスの正常終了");
                    }
                }
                catch (Exception error)
                {
                    failures++;
                    Console.Error.WriteLine($"専用プロセスを正常終了できませんでした。PID={fixture.Id}\n{error}");
                }
                finally { fixture.Dispose(); }
            }
        }
        Console.WriteLine($"実機検証: {successes}/{successes + failures} 成功");
        return failures == 0 ? 0 : 1;
    }

    private static Process StartFixture()
    {
        string assembly = typeof(NativeTests).Assembly.Location;
        string executable = Path.ChangeExtension(assembly, ".exe");
        bool useAppHost = File.Exists(executable);
        var start = new ProcessStartInfo(useAppHost ? executable : "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(assembly)!
        };
        if (!useAppHost)
            start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--fixture");
        return Process.Start(start) ?? throw new InvalidOperationException("専用プロセスを起動できませんでした。");
    }

    private static WindowState ReadState(NativeWindowService service, WindowIdentity target) =>
        service.GetState(target) ?? throw new InvalidOperationException("専用ウィンドウが終了しました。");

    private static WindowState WaitForState(NativeWindowService service, WindowIdentity target,
        Func<WindowState, bool> expected, string operation)
    {
        WindowState? last = null;
        int stable = 0;
        WaitUntil(() =>
        {
            var actual = ReadState(service, target);
            stable = actual == last ? stable + 1 : 0;
            last = actual;
            return expected(actual) && stable >= 3;
        }, operation);
        return last!;
    }

    private static void WaitUntil(Func<bool> ready, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            if (ready())
                return;
            Thread.Sleep(30);
        }
        throw new TimeoutException($"待機時間内に完了しませんでした: {operation}");
    }

    private static void PumpFor(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"想定した例外 {typeof(TException).Name} が発生しませんでした。");
    }
}
