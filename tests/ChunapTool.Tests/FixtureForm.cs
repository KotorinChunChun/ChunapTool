namespace ChunapTool.Tests;

/// <summary>実機検証で他のアプリを動かさずに済む、独立した操作対象。</summary>
internal sealed class FixtureForm : BrandedForm
{
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 250 };

    public FixtureForm()
    {
        Text = WindowTitle.Create("動作確認用");
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(250, 180);
        Size = new Size(620, 400);
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(Math.Max(area.Left, area.Right - Width - 24), area.Top + 48);
        BackColor = Color.FromArgb(19, 32, 51);
        ForeColor = Color.White;
        Font = new Font("Yu Gothic UI", 12);
        DoubleBuffered = true;
        _clock.Tick += (_, _) => Invalidate();
        _clock.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int stripeHeight = Math.Max(1, ClientSize.Height / 3);
        using var upper = new SolidBrush(Color.FromArgb(30, 100, 160));
        using var middle = new SolidBrush(Color.FromArgb(40, 145, 110));
        e.Graphics.FillRectangle(upper, 0, 0, ClientSize.Width, stripeHeight);
        e.Graphics.FillRectangle(middle, 0, stripeHeight, ClientSize.Width, stripeHeight);

        string message = $"ChunapTool 動作確認用\n\n{DateTime.Now:HH:mm:ss.fff}\n"
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
}
