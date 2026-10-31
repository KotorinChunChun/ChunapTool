using System.Globalization;
using System.Runtime.InteropServices;

namespace ChunapTool.Tests;

/// <summary>確認用ウィンドウの起動条件。CLI検証では、テストごとに名前・大きさ・挙動の違うウィンドウを起動する。</summary>
internal sealed record FixtureOptions
{
    public string Title { get; init; } = "動作確認用";
    public Size? ClientSize { get; init; }
    public Point? Position { get; init; }
    public Size? MinimumSize { get; init; }
    public bool TopMost { get; init; }
    // 表示後にUIスレッドを止め続け、応答なしの対象を再現する。
    public bool Hung { get; init; }
    // 表示後は位置・サイズの変更を受け付けず、反映を確認できない対象を再現する。
    public bool Locked { get; init; }

    internal static FixtureOptions Parse(IReadOnlyList<string> args)
    {
        string? Value(string name)
        {
            var index = args.ToList().IndexOf(name);
            return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
        }
        static Size? ParseSize(string? text) => text?.Split('x') is [var w, var h]
            ? new Size(int.Parse(w, CultureInfo.InvariantCulture), int.Parse(h, CultureInfo.InvariantCulture)) : null;
        static Point? ParsePoint(string? text) => text?.Split(',') is [var x, var y]
            ? new Point(int.Parse(x, CultureInfo.InvariantCulture), int.Parse(y, CultureInfo.InvariantCulture)) : null;
        return new FixtureOptions
        {
            Title = Value("--title") ?? "動作確認用",
            ClientSize = ParseSize(Value("--client")),
            Position = ParsePoint(Value("--position")),
            MinimumSize = ParseSize(Value("--min")),
            TopMost = args.Contains("--topmost"),
            Hung = args.Contains("--hung"),
            Locked = args.Contains("--locked"),
        };
    }
}

/// <summary>実機検証で他のアプリを動かさずに済む、独立した操作対象。</summary>
internal sealed class FixtureForm : BrandedForm
{
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 250 };
    private readonly FixtureOptions _options;
    private bool _locked;

    public FixtureForm() : this(new FixtureOptions()) { }

    internal FixtureForm(FixtureOptions options)
    {
        _options = options;
        Text = WindowTitle.Create(options.Title);
        StartPosition = FormStartPosition.Manual;
        MinimumSize = options.MinimumSize ?? new Size(250, 180);
        Size = new Size(620, 400);
        if (options.ClientSize is { } client) ClientSize = client;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = options.Position ?? new Point(Math.Max(area.Left, area.Right - Width - 24), area.Top + 48);
        TopMost = options.TopMost;
        BackColor = Color.FromArgb(19, 32, 51);
        ForeColor = Color.White;
        Font = new Font("Yu Gothic UI", 12);
        DoubleBuffered = true;
        _clock.Tick += (_, _) => Invalidate();
        _clock.Start();
        Shown += (_, _) =>
        {
            _locked = options.Locked;
            if (options.Hung) BeginInvoke(() => { while (true) Thread.Sleep(60000); });
        };
    }

    protected override void WndProc(ref Message m)
    {
        const int WindowPosChanging = 0x0046;
        if (_locked && m.Msg == WindowPosChanging)
        {
            // SWP_NOMOVE | SWP_NOSIZEを加え、外部からの移動・サイズ変更を無視する。
            var position = Marshal.PtrToStructure<WindowPos>(m.LParam);
            position.Flags |= 0x0002 | 0x0001;
            Marshal.StructureToPtr(position, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int stripeHeight = Math.Max(1, ClientSize.Height / 3);
        using var upper = new SolidBrush(Color.FromArgb(30, 100, 160));
        using var middle = new SolidBrush(Color.FromArgb(40, 145, 110));
        e.Graphics.FillRectangle(upper, 0, 0, ClientSize.Width, stripeHeight);
        e.Graphics.FillRectangle(middle, 0, stripeHeight, ClientSize.Width, stripeHeight);

        string message = $"ChunapTool {_options.Title}\n\n{DateTime.Now:HH:mm:ss.fff}\n"
            + $"位置: {Left}, {Top}   外枠: {Width} × {Height}\n"
            + "この画面を選択して移動・サイズ変更・Undoを確認します。";
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.DrawString(message, Font, brush, new RectangleF(18, 16,
            Math.Max(1, ClientSize.Width - 36), Math.Max(1, ClientSize.Height - 32)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
        }

        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Handle, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
}
