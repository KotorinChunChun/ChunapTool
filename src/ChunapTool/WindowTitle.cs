namespace ChunapTool;

public static class WindowTitle
{
    public static string Create(string? screen = null)
    {
        var version = typeof(WindowTitle).Assembly.GetName().Version!;
        var title = $"ChunapTool - ver {version.Major}.{version.Minor}.{version.Build}";
        return string.IsNullOrWhiteSpace(screen) ? title : $"{screen} - {title}";
    }
}
