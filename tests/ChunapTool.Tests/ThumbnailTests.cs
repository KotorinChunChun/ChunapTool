using ChunapTool.Core;
using ChunapTool.Thumbnails;
using System.Diagnostics;
using System.Reflection;

namespace ChunapTool.Tests;

internal static class ThumbnailTests
{
    private static readonly WindowEntry First = new(new WindowIdentity(0, 1), "Alpha 対象", "alpha.exe");
    private static readonly WindowEntry Second = new(new WindowIdentity(0, 2), "Beta 対象", "beta.exe");
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    internal static int Run()
    {
        (string, Action)[] tests =
        [
            ("検索は撮影せず大文字小文字・名前変更・0件を即反映", SearchWithoutCapture),
            ("撮影は1件ずつ順番に行い5秒未満で再取得しない", RefreshOrderAndInterval),
            ("画面内カードを優先し絞込外は撮影しない", VisibleAndFilteredPriority),
            ("撮影中の並列要求は実行せず結果を一度だけ採用", SingleInFlight),
            ("候補削除と同一HWND別PIDの結果を混在させない", RejectRemovedResult),
            ("閉じた後に遅れて届く画像を破棄し撮影停止", DisposeDuringCapture),
            ("画像の入替・候補削除・終了時に所有画像を解放", ImageOwnership),
            ("撮影失敗時もカードを残し過剰再試行しない", CaptureFailure),
            ("一覧は名前と画像を配置し列数・即時検索を切替", PickerLayoutAndSearch),
            ("カード選択は現在対象を確認して確定し撮影を停止", PickerSelection),
            ("一覧の取消と終了対象選択は現在対象を返さない", PickerCancelAndClosedTarget),
            ("一覧を閉じると待機撮影を取消し結果を解放", PickerCloseDuringCapture),
        ];
        int failures = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine("成功: " + name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine($"失敗: {name}\n{ex}"); }
        }
        Console.WriteLine($"サムネイル検証: {tests.Length - failures}/{tests.Length} 成功");
        return failures == 0 ? 0 : 1;
    }

    private static WindowIdentity[] Both => [First.Identity, Second.Identity];
    private static Task<WindowIdentity?> Refresh(ThumbnailCatalog model, DateTimeOffset? now = null) =>
        model.RefreshNextAsync(Both, Both, now ?? Now, CancellationToken.None);

    private static void SearchWithoutCapture()
    {
        using var capture = new FakeCapture();
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First, Second]);
        Equal(1, model.Filter("ALPHA").Count);
        Equal(1, model.Filter("BETA.EXE").Count);
        Equal(0, model.Filter("なし").Count);
        model.UpdateEntries([First with { Title = "改名した対象" }, Second]);
        Equal(1, model.Filter("改名").Count);
        Equal(0, capture.Calls.Count);
    }

    private static void RefreshOrderAndInterval()
    {
        using var capture = new FakeCapture();
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First, Second]);
        Equal(First.Identity, Await(Refresh(model))!.Value);
        Equal(Second.Identity, Await(Refresh(model))!.Value);
        True(Await(Refresh(model, Now.AddSeconds(4.9))) is null);
        Equal(2, capture.Calls.Count);
        Equal(First.Identity, Await(Refresh(model, Now.AddSeconds(5)))!.Value);
        Equal(ThumbnailCatalog.MaximumImageSize, capture.LastMaximumSize);
    }

    private static void VisibleAndFilteredPriority()
    {
        using var capture = new FakeCapture();
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First, Second]);
        Equal(Second.Identity, Await(model.RefreshNextAsync([Second.Identity], Both, Now, CancellationToken.None))!.Value);
        Equal(Second.Identity, Await(model.RefreshNextAsync([], [Second.Identity], Now.AddSeconds(6), CancellationToken.None))!.Value);
        True(capture.Calls.All(id => id == Second.Identity));
    }

    private static void SingleInFlight()
    {
        using var capture = new FakeCapture { Block = true };
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First, Second]);
        var first = Refresh(model);
        True(!first.IsCompleted);
        True(Await(Refresh(model)) is null);
        Equal(1, capture.Calls.Count);
        capture.Complete();
        Equal(First.Identity, Await(first)!.Value);
        Equal(1, capture.MaximumConcurrent);
    }

    private static void RejectRemovedResult()
    {
        using var capture = new FakeCapture { Block = true };
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First]);
        var task = Refresh(model);
        model.UpdateEntries([new WindowEntry(new WindowIdentity(0, 99), "別PID", "other.exe")]);
        var bitmap = capture.Complete();
        True(Await(task) is null);
        IsDisposed(bitmap);
        Equal(1, model.Filter("").Count);
        True(model.Filter("")[0].Image is null);
    }

    private static void DisposeDuringCapture()
    {
        using var capture = new FakeCapture { Block = true };
        var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First]);
        var task = Refresh(model);
        model.Dispose();
        True(capture.Token.IsCancellationRequested && capture.Disposed);
        var bitmap = capture.Complete();
        True(Await(task) is null);
        IsDisposed(bitmap);
        Equal(0, model.Filter("").Count);
        True(Await(Refresh(model)) is null);
        model.Dispose();
    }

    private static void ImageOwnership()
    {
        using var capture = new FakeCapture();
        var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First, Second]);
        Await(Refresh(model));
        var firstImage = model.Filter("")[0].Image!;
        Await(model.RefreshNextAsync([], [First.Identity], Now.AddSeconds(6), CancellationToken.None));
        IsDisposed(firstImage);
        var replacement = model.Filter("")[0].Image!;
        model.UpdateEntries([Second]);
        IsDisposed(replacement);
        Await(model.RefreshNextAsync([], [Second.Identity], Now.AddSeconds(6), CancellationToken.None));
        var final = model.Filter("")[0].Image!;
        model.Dispose();
        IsDisposed(final);
    }

    private static void CaptureFailure()
    {
        using var capture = new FakeCapture { Fail = true };
        using var model = new ThumbnailCatalog(capture);
        model.UpdateEntries([First]);
        Equal(First.Identity, Await(Refresh(model))!.Value);
        Equal(1, model.Filter("").Count);
        True(model.Filter("")[0].Status.Contains("検証用失敗", StringComparison.Ordinal));
        True(Await(Refresh(model, Now.AddSeconds(1))) is null);
        Equal(1, capture.Calls.Count);
    }

    private static void PickerLayoutAndSearch()
    {
        using var screen = new PickerScreen();
        Equal(2, screen.Grid.Controls.Count);
        Equal(0, screen.Capture.Calls.Count);
        Equal(WindowTitle.Create("サムネイルから対象を選択"), screen.Form.Text);
        var cards = screen.Grid.Controls.Cast<ThumbnailCard>().ToArray();
        Equal(First.Title, cards[0].AccessibleName);
        True(cards[0].IsCurrent);
        True(cards.All(card => card.Width > 0 && card.Height > 0));
        int columns = screen.Grid.ColumnCount;
        screen.Form.ClientSize = new Size(660, 500);
        Application.DoEvents();
        True(screen.Grid.ColumnCount < columns);
        foreach (var card in screen.Grid.Controls.Cast<ThumbnailCard>())
            True(screen.Grid.ClientRectangle.Contains(card.Bounds));
        screen.Search.Text = "BETA";
        Equal(1, screen.Grid.Controls.Count);
        Equal(Second.Identity, ((ThumbnailCard)screen.Grid.Controls[0]).Tile.Entry.Identity);
        Equal(1, screen.Windows.Enumerations);
        screen.Search.Text = "なし";
        Equal(0, screen.Grid.Controls.Count);
        True(screen.Field<Label>("count").Text.Contains("ありません", StringComparison.Ordinal));
        Equal(0, screen.Capture.Calls.Count);
    }

    private static void PickerSelection()
    {
        using var screen = new PickerScreen();
        screen.Search.Text = "Beta";
        screen.Invoke("SelectEntry", Second);
        Equal(DialogResult.OK, screen.Form.DialogResult);
        Equal(Second, screen.Form.SelectedEntry);
        True(screen.Capture.Disposed);
        Equal(0, screen.Windows.Mutations);
    }

    private static void PickerCancelAndClosedTarget()
    {
        using (var screen = new PickerScreen())
        {
            screen.Windows.Entries.Remove(Second);
            screen.Invoke("SelectEntry", Second);
            True(screen.Form.SelectedEntry is null && !screen.Form.IsDisposed);
            Equal(1, screen.Grid.Controls.Count);
            var message = Message.Create(screen.Form.Handle, 0x100, (nint)Keys.Escape, 0);
            typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(screen.Form, [Keys.Escape]);
            True(screen.Form.SelectedEntry is null);
            True(screen.Form.IsDisposed);
            True(screen.Capture.Disposed);
        }
    }

    private static void PickerCloseDuringCapture()
    {
        using var screen = new PickerScreen(block: true);
        var task = (Task)screen.Invoke("RefreshOneAsync")!;
        True(!task.IsCompleted);
        screen.Form.Close();
        var bitmap = screen.Capture.Complete();
        Await(task);
        IsDisposed(bitmap);
        True(screen.Capture.Token.IsCancellationRequested && screen.Capture.Disposed);
    }

    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Await(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.ElapsedMilliseconds > 4000) throw new TimeoutException("検証待機がタイムアウトしました。");
            Application.DoEvents(); Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"期待={expected}, 実際={actual}"); }
    private static void True(bool value) { if (!value) throw new InvalidOperationException("条件を満たしません。"); }
    private static void IsDisposed(Bitmap bitmap)
    {
        try { bitmap.GetPixel(0, 0); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("画像が解放されていません。");
    }

    private sealed class PickerScreen : IDisposable
    {
        internal FakeWindows Windows { get; } = new();
        internal FakeCapture Capture { get; }
        internal ThumbnailPickerForm Form { get; }
        internal TableLayoutPanel Grid => Field<TableLayoutPanel>("grid");
        internal TextBox Search => Field<TextBox>("search");
        internal PickerScreen(bool block = false)
        {
            Capture = new FakeCapture { Block = block };
            Form = new ThumbnailPickerForm(Windows, Capture, First.Identity, "", passive: true);
            True(Form.Opacity == 0 && !Form.TopMost && !Form.ShowInTaskbar);
            Form.Show(); Application.DoEvents();
            Field<System.Windows.Forms.Timer>("captureTimer").Stop();
            Field<System.Windows.Forms.Timer>("catalogTimer").Stop();
        }
        internal T Field<T>(string name) => (T)typeof(ThumbnailPickerForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Form)!;
        internal object? Invoke(string name, params object[] arguments) => typeof(ThumbnailPickerForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Form, arguments);
        public void Dispose() { Form.Close(); Form.Dispose(); Application.DoEvents(); }
    }

    private sealed class FakeCapture : IThumbnailCapture
    {
        internal List<WindowIdentity> Calls { get; } = [];
        internal Size LastMaximumSize { get; private set; }
        internal bool Block { get; init; }
        internal bool Fail { get; init; }
        internal bool Disposed { get; private set; }
        internal CancellationToken Token { get; private set; }
        private int concurrent;
        internal int MaximumConcurrent { get; private set; }
        private readonly TaskCompletionSource<ThumbnailCaptureResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ThumbnailCaptureResult> CaptureAsync(WindowIdentity target, Size maximumSize, CancellationToken token)
        {
            Calls.Add(target); Token = token; LastMaximumSize = maximumSize;
            MaximumConcurrent = Math.Max(MaximumConcurrent, ++concurrent);
            try
            {
                if (Fail) throw new InvalidOperationException("検証用失敗");
                if (Block) return await pending.Task;
                return new ThumbnailCaptureResult(new Bitmap(8, 6), "");
            }
            finally { concurrent--; }
        }
        internal Bitmap Complete()
        {
            var bitmap = new Bitmap(8, 6);
            pending.SetResult(new ThumbnailCaptureResult(bitmap, ""));
            return bitmap;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeWindows : IWindowService
    {
        internal List<WindowEntry> Entries { get; } = [First, Second];
        internal int Enumerations { get; private set; }
        internal int Mutations { get; private set; }
        public bool IsLeftButtonDown => false;
        public IReadOnlyList<WindowEntry> Enumerate(string filter) { Enumerations++; return Entries.Where(entry => WindowCandidates.Matches(entry, filter)).ToArray(); }
        public WindowState? GetState(WindowIdentity target) => Entries.Any(entry => entry.Identity == target)
            ? new WindowState(target, new WindowBounds(0, 0, 400, 300), new WindowPlacement(0, 1, default, default, new WindowBounds(0, 0, 400, 300)), true) : null;
        public WindowBounds GetWorkArea(WindowIdentity target) => new(0, 0, 1920, 1080);
        public void ApplyBounds(WindowIdentity target, WindowBounds bounds) => Mutations++;
        public void SetMaximized(WindowIdentity target, bool maximized) => Mutations++;
        public void Restore(WindowState state) => Mutations++;
    }
}
