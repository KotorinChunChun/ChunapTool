using ChunapTool.Core;
using ChunapTool.Native;
using ChunapTool.Thumbnails;
using System.Diagnostics;

namespace ChunapTool.Tests;

internal static class WgcSmokeTests
{
    internal static int Run(string output)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var target = new CaptureFixture();
        using var capture = new WgcThumbnailCapture();
        int passed = 0;
        try
        {
            Directory.CreateDirectory(output);
            target.Show();
            Application.DoEvents();
            var identity = new WindowIdentity(target.Handle, (uint)Environment.ProcessId);
            using (var result = Await(capture.CaptureAsync(identity, new Size(320, 200), CancellationToken.None)))
            {
                result.Image?.Save(Path.Combine(output, "wgc-400x240.png"));
                var image = Validate(result);
                Console.WriteLine($"成功: WGC静止画 {image.Width}x{image.Height} / 対象外枠 {target.Width}x{target.Height}");
                passed++;
            }
            target.Size = new Size(640, 400);
            target.Invalidate();
            Application.DoEvents();
            using (var result = Await(capture.CaptureAsync(identity, new Size(320, 200), CancellationToken.None)))
            {
                result.Image?.Save(Path.Combine(output, "wgc-640x400.png"));
                var image = Validate(result);
                Console.WriteLine($"成功: サイズ変更後のWGC静止画 {image.Width}x{image.Height}");
                passed++;
            }
            using (var result = Await(capture.CaptureAsync(identity with { ProcessId = identity.ProcessId + 1 }, new Size(320, 200), CancellationToken.None)))
            {
                if (result.Image is not null || !result.Status.Contains("終了", StringComparison.Ordinal)) throw new InvalidOperationException("PID不一致を拒否しませんでした。");
                Console.WriteLine("成功: PID不一致の撮影拒否");
                passed++;
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { using var unexpected = Await(capture.CaptureAsync(identity, new Size(320, 200), cancellation.Token)); throw new InvalidOperationException("キャンセルされませんでした。"); }
                catch (OperationCanceledException) { Console.WriteLine("成功: 撮影前のキャンセル"); passed++; }
            }
            target.Close();
            using (var result = Await(capture.CaptureAsync(identity, new Size(320, 200), CancellationToken.None)))
            {
                if (result.Image is not null) throw new InvalidOperationException("終了した対象を撮影しました。");
                Console.WriteLine("成功: 対象終了後の撮影拒否");
                passed++;
            }
            Console.WriteLine($"WGC実取得検証: {passed}/5 成功。背面・非アクティブの専用フォームのみ。他アプリへの入力なし。");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"WGC実取得検証失敗（{passed}/5成功）:\n{ex}"); return 1; }
        finally { target.Close(); }
    }

    private static Bitmap Validate(ThumbnailCaptureResult result)
    {
        if (result.Image is not { } bitmap) throw new InvalidOperationException("画像を取得できません: " + result.Status);
        if (bitmap.Width > 320 || bitmap.Height > 200 || bitmap.Width < 2 || bitmap.Height < 2) throw new InvalidOperationException("縮小画像の寸法が不正です。");
        bool red = false, green = false;
        for (int y = 0; y < bitmap.Height; y += 2)
        for (int x = 0; x < bitmap.Width; x += 2)
        {
            var color = bitmap.GetPixel(x, y);
            red |= color.R > 150 && color.G < 100 && color.B < 100;
            green |= color.G > 130 && color.R < 100 && color.B < 100;
        }
        if (!red || !green) throw new InvalidOperationException("専用ウィンドウに描画した赤と緑を画像で確認できません。");
        return bitmap;
    }

    private static T Await<T>(Task<T> task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.ElapsedMilliseconds > 8000) throw new TimeoutException("撮影を8秒待っても完了しません。");
            Application.DoEvents(); Thread.Sleep(5);
        }
        return task.GetAwaiter().GetResult();
    }

    private sealed class CaptureFixture : BrandedForm
    {
        internal CaptureFixture()
        {
            Text = WindowTitle.Create("WGC専用検証");
            StartPosition = FormStartPosition.Manual;
            Size = new Size(400, 240);
            var area = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            ShowInTaskbar = false;
            TopMost = false;
        }
        protected override bool ShowWithoutActivation => true;
        protected override void SetVisibleCore(bool value)
        {
            if (value) NativeMethods.SetWindowPos(Handle, 1, Left, Top, Width, Height, 0x0010);
            base.SetVisibleCore(value);
            if (value) NativeMethods.SetWindowPos(Handle, 1, Left, Top, Width, Height, 0x0010);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.FromArgb(210, 20, 20));
            using var green = new SolidBrush(Color.FromArgb(20, 210, 20));
            e.Graphics.FillRectangle(green, ClientSize.Width / 2, 0, ClientSize.Width / 2, ClientSize.Height);
        }
    }
}
