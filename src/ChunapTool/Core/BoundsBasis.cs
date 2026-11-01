using System.Globalization;

namespace ChunapTool.Core;

/// <summary>位置・サイズの基準。Frameは見える枠、Clientはクライアント領域（タイトルバーと枠を除く中身）。</summary>
public enum BoundsBasis { Frame, Client }

public static class BoundsBases
{
    public static string Name(BoundsBasis basis) => basis == BoundsBasis.Client ? "client" : "frame";

    public static bool TryParse(string? text, out BoundsBasis basis)
    {
        basis = BoundsBasis.Frame;
        switch (text)
        {
            case "frame": return true;
            case "client": basis = BoundsBasis.Client; return true;
            default: return false;
        }
    }

    /// <summary>状態を指定の基準で読む。クライアント領域を取得できない状態では見える枠を返す。</summary>
    public static WindowBounds Read(WindowState state, BoundsBasis basis) =>
        basis == BoundsBasis.Client && state.ClientBounds is { } client ? client : state.Bounds;

    /// <summary>基準の矩形を見える枠へ換算する。見える枠とクライアント領域の4辺の差を、現在の状態から求める。</summary>
    public static WindowBounds ToFrame(WindowBounds bounds, WindowState state, BoundsBasis basis)
    {
        if (basis == BoundsBasis.Frame || state.ClientBounds is not { } client) return bounds;
        var frame = state.Bounds;
        int left = client.X - frame.X, top = client.Y - frame.Y;
        int right = frame.X + frame.Width - (client.X + client.Width);
        int bottom = frame.Y + frame.Height - (client.Y + client.Height);
        return new WindowBounds(bounds.X - left, bounds.Y - top, bounds.Width + left + right, bounds.Height + top + bottom);
    }

    /// <summary>見える枠の矩形を、現在の状態の4辺の差で基準の矩形へ戻す。</summary>
    public static WindowBounds FromFrame(WindowBounds frame, WindowState state, BoundsBasis basis)
    {
        if (basis == BoundsBasis.Frame || state.ClientBounds is not { } client) return frame;
        var current = state.Bounds;
        int left = client.X - current.X, top = client.Y - current.Y;
        int right = current.X + current.Width - (client.X + client.Width);
        int bottom = current.Y + current.Height - (client.Y + client.Height);
        return new WindowBounds(frame.X + left, frame.Y + top, frame.Width - left - right, frame.Height - top - bottom);
    }
}

/// <summary>縦横比を保つサイズの計算。四捨五入し、最小サイズを満たさない辺はもう一方から逆算する。</summary>
public static class AspectRatio
{
    /// <summary>「16:9」「4:3」「1.5」の形式。正の有限値だけを受け付ける。</summary>
    public static bool TryParse(string? text, out double ratio)
    {
        ratio = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(':');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0)
            ratio = w / h;
        else if (parts.Length == 1)
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out ratio);
        return double.IsFinite(ratio) && ratio > 0;
    }

    public static double Of(int width, int height) => (double)Math.Max(1, width) / Math.Max(1, height);

    /// <summary>幅（または高さ）を基準にもう一方を求める。両方nullなら現在の幅を基準にする。</summary>
    public static (int Width, int Height) Fit(int? width, int? height, double ratio, int currentWidth, int minimum = WindowGeometry.MinimumSize)
    {
        if (width is null && height is not null)
        {
            int h = Math.Max(minimum, height.Value);
            int w = Round(h * ratio);
            return w >= minimum ? (w, h) : (minimum, Math.Max(minimum, Round(minimum / ratio)));
        }
        int fixedWidth = Math.Max(minimum, width ?? currentWidth);
        int fittedHeight = Round(fixedWidth / ratio);
        return fittedHeight >= minimum ? (fixedWidth, fittedHeight) : (Math.Max(minimum, Round(minimum * ratio)), minimum);
    }

    /// <summary>比率を保ったまま、最大の幅・高さに収まるよう縮める。収まっていればそのまま返す。</summary>
    public static (int Width, int Height) Shrink(int width, int height, double ratio, int maxWidth, int maxHeight, int minimum = WindowGeometry.MinimumSize)
    {
        if (width <= maxWidth && height <= maxHeight) return (width, height);
        int w = Math.Min(width, maxWidth);
        int h = Round(w / ratio);
        if (h > maxHeight) { h = maxHeight; w = Round(h * ratio); }
        return (Math.Max(minimum, w), Math.Max(minimum, h));
    }

    private static int Round(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
}
