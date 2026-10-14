using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using ChunapTool.Core;

namespace ChunapTool.Native;

/// <summary>DWMのウィンドウ全体サムネイルを、親フォーム内へ縦横比を保って表示する。</summary>
public sealed class DwmPreview : IDisposable
{
    private nint _thumbnail;
    private WindowIdentity _source;
    private bool _disposed;

    public bool IsAttached => _thumbnail != 0;

    public void Attach(nint destination, nint source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.IsWindow(destination) || !NativeMethods.IsWindow(source))
            throw new Win32Exception(NativeMethods.InvalidWindowHandle, "プレビュー対象のウィンドウが見つかりません。");
        if (NativeMethods.GetAncestor(destination, 2) != destination || NativeMethods.GetAncestor(source, 2) != source)
            throw new ArgumentException("プレビューにはトップレベルウィンドウを指定してください。");
        NativeMethods.GetWindowThreadProcessId(destination, out uint destinationProcess);
        if (destinationProcess != (uint)Environment.ProcessId)
            throw new ArgumentException("プレビューの表示先は、このアプリのウィンドウである必要があります。", nameof(destination));
        NativeMethods.GetWindowThreadProcessId(source, out uint sourceProcess);
        Clear();
        CheckResult(NativeMethods.DwmRegisterThumbnail(destination, source, out _thumbnail),
            "プレビューを開始できませんでした。");
        _source = new WindowIdentity(source, sourceProcess);
    }

    public void Update(Rectangle area, bool visible = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thumbnail == 0)
            return;
        if (!NativeWindowService.Matches(_source))
            throw new Win32Exception(NativeMethods.InvalidWindowHandle, "プレビュー対象のウィンドウが終了しました。");

        bool show = visible && area.Width > 0 && area.Height > 0;
        var destination = area;
        if (show)
        {
            CheckResult(NativeMethods.DwmQueryThumbnailSourceSize(_thumbnail, out var size),
                "プレビューの元サイズを取得できませんでした。");
            show = size.Width > 0 && size.Height > 0;
            if (show)
            {
                double scale = Math.Min((double)area.Width / size.Width, (double)area.Height / size.Height);
                int width = Math.Max(1, (int)Math.Floor(size.Width * scale));
                int height = Math.Max(1, (int)Math.Floor(size.Height * scale));
                destination = new Rectangle(area.X + (area.Width - width) / 2,
                    area.Y + (area.Height - height) / 2, width, height);
            }
        }
        var properties = new NativeMethods.ThumbnailProperties
        {
            Flags = 0x0001 | 0x0004 | 0x0008 | 0x0010,
            Destination = new NativeMethods.Rect
            {
                Left = destination.Left, Top = destination.Top,
                Right = destination.Right, Bottom = destination.Bottom
            },
            Opacity = 255,
            Visible = show,
            SourceClientAreaOnly = false
        };
        CheckResult(NativeMethods.DwmUpdateThumbnailProperties(_thumbnail, in properties),
            "プレビューの表示を更新できませんでした。");
    }

    public void Clear()
    {
        if (_thumbnail == 0)
            return;
        nint thumbnail = _thumbnail;
        _thumbnail = 0;
        _source = default;
        // 対象や表示先の終了後はDWM側で解除済みの場合がある。終了処理の失敗へ伝播させない。
        int result = NativeMethods.DwmUnregisterThumbnail(thumbnail);
        if (result < 0)
            System.Diagnostics.Debug.WriteLine($"プレビュー解除時のDWM応答: 0x{result:X8}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        try { Clear(); }
        finally { _disposed = true; }
        GC.SuppressFinalize(this);
    }

    private static void CheckResult(int result, string message)
    {
        if (result < 0)
            throw new COMException(message, result);
    }
}
