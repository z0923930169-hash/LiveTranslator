using System.Drawing;
using System.Windows.Forms;

namespace LiveTranslator;

public sealed class SubtitleOverlay : Form
{
    private readonly Label label;
    private Point dragOrigin;
    private Point windowOrigin;

    public SubtitleOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.82;
        StartPosition = FormStartPosition.Manual;
        Width = 1100;
        Height = 160;
        Left = 100;
        Top = 760;

        label = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.Black,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft JhengHei UI", 28, FontStyle.Bold),
            Text = "等待語音…",
            Padding = new Padding(20)
        };
        Controls.Add(label);

        MouseDown += BeginDrag;
        MouseMove += DragMove;
        label.MouseDown += BeginDrag;
        label.MouseMove += DragMove;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragOrigin = Cursor.Position;
        windowOrigin = Location;
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var delta = new Point(Cursor.Position.X - dragOrigin.X, Cursor.Position.Y - dragOrigin.Y);
        Location = new Point(windowOrigin.X + delta.X, windowOrigin.Y + delta.Y);
    }

    public void SetSubtitle(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetSubtitle(text)); return; }
        label.Text = string.IsNullOrWhiteSpace(text) ? "…" : text;
    }

    public void SetFontSize(float size)
    {
        if (InvokeRequired) { BeginInvoke(() => SetFontSize(size)); return; }
        label.Font = new Font("Microsoft JhengHei UI", Math.Clamp(size, 16, 52), FontStyle.Bold);
    }

    public void SetOpacityPercent(int percent)
    {
        Opacity = Math.Clamp(percent / 100.0, 0.3, 1.0);
    }
}