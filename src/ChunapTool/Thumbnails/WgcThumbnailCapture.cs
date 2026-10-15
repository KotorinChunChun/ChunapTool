using ChunapTool.Core;
using ChunapTool.Native;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace ChunapTool.Thumbnails;

/// <summary>1対象から1フレームだけ取得する。画面全体のコピーへは切り替えない。</summary>
internal sealed class WgcThumbnailCapture : IThumbnailCapture
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim singleCapture = new(1, 1);
    private bool disposed;

    public async Task<ThumbnailCaptureResult> CaptureAsync(WindowIdentity target, Size maximumSize, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(2));
        await singleCapture.WaitAsync(linked.Token);
        try { return await Task.Run(() => Capture(target, maximumSize, linked.Token), linked.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !disposed)
        { return new(null, "取得待ちがタイムアウトしました"); }
        finally { singleCapture.Release(); }
    }

    private static ThumbnailCaptureResult Capture(WindowIdentity target, Size maximumSize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (maximumSize.Width < 1 || maximumSize.Height < 1) throw new ArgumentOutOfRangeException(nameof(maximumSize));
        var state = new NativeWindowService().GetState(target);
        if (state is null) return new(null, "対象は終了しました");
        if (state.IsMinimized) return new(null, "最小化中");
        int initialized = RoInitialize(1);
        if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
        try
        {
            if (!GraphicsCaptureSession.IsSupported()) return new(null, "この環境では撮影を利用できません");
            var item = CaptureDirect3D.CreateWindowItem(target.Handle);
            ValidateSize(item.Size.Width, item.Size.Height);
            token.ThrowIfCancellationRequested();
            using var graphics = new CaptureDirect3D();
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(graphics.ProjectedDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
            using var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            session.StartCapture();
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 1500)
            {
                token.ThrowIfCancellationRequested();
                if (!NativeWindowService.Matches(target)) return new(null, "対象は終了しました");
                using var frame = pool.TryGetNextFrame();
                if (frame is not null)
                {
                    var size = frame.ContentSize;
                    ValidateSize(size.Width, size.Height);
                    if (size.Width != item.Size.Width || size.Height != item.Size.Height)
                        return new(null, "サイズ変更中。次回更新で再取得します");
                    using var surface = frame.Surface;
                    using var full = graphics.ReadBitmap(surface, size.Width, size.Height, token.ThrowIfCancellationRequested);
                    token.ThrowIfCancellationRequested();
                    if (!NativeWindowService.Matches(target)) return new(null, "対象は終了しました");
                    double ratio = Math.Min(1, Math.Min((double)maximumSize.Width / full.Width, (double)maximumSize.Height / full.Height));
                    var result = new Bitmap(Math.Max(1, (int)(full.Width * ratio)), Math.Max(1, (int)(full.Height * ratio)));
                    try
                    {
                        using var drawing = Graphics.FromImage(result);
                        drawing.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        drawing.DrawImage(full, new Rectangle(Point.Empty, result.Size));
                        return new(result, "");
                    }
                    catch { result.Dispose(); throw; }
                }
                if (token.WaitHandle.WaitOne(15)) token.ThrowIfCancellationRequested();
            }
            return new(null, "取得待ちがタイムアウトしました");
        }
        finally { RoUninitialize(); }
    }

    private static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 32766 || height > 32766 || (long)width * height > 32L * 1024 * 1024)
            throw new InvalidOperationException("対象の画像サイズが取得可能な範囲を超えています。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        // 撮影途中でもUIを待たせない。取得側のusingがGPU・セッション・フレームを解放する。
    }

    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
}
