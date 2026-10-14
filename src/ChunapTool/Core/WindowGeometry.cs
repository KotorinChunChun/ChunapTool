namespace ChunapTool.Core;

/// <summary>見えるウィンドウ枠をモニターの作業領域へ収める計算。</summary>
public static class WindowGeometry
{
    public const int MinimumSize = 32;

    public static WindowBounds Constrain(WindowBounds requested, WindowBounds workArea, bool allowOutOfBounds)
    {
        int width = Math.Max(MinimumSize, requested.Width);
        int height = Math.Max(MinimumSize, requested.Height);
        if (allowOutOfBounds)
        {
            return requested with { Width = width, Height = height };
        }

        // OSから有効な領域を得られない場合にも、ゼロや負のサイズを返さない。
        int areaWidth = Math.Max(1, workArea.Width);
        int areaHeight = Math.Max(1, workArea.Height);
        width = Math.Min(width, areaWidth);
        height = Math.Min(height, areaHeight);

        // 負座標のモニターとint境界をまたぐ領域でも右端・下端を安全に計算する。
        long maxX = (long)workArea.X + areaWidth - width;
        long maxY = (long)workArea.Y + areaHeight - height;
        int x = (int)Math.Clamp((long)requested.X, workArea.X, maxX);
        int y = (int)Math.Clamp((long)requested.Y, workArea.Y, maxY);
        return new WindowBounds(x, y, width, height);
    }
}
