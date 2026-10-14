using System.Runtime.InteropServices;

namespace ChunapTool;

public class BrandedForm : Form
{
    private readonly Icon productIcon;

    public BrandedForm()
    {
        using var stream = typeof(BrandedForm).Assembly.GetManifestResourceStream("ChunapTool.assets.ChulipVideo.png")!;
        using var source = new Bitmap(stream);
        using var bitmap = new Bitmap(source, 32, 32);
        var handle = bitmap.GetHicon();
        try { productIcon = (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
        Icon = productIcon;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) productIcon.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
