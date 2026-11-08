using System.Drawing.Drawing2D;

namespace BenchAutoClicker;

internal sealed class SessionProgressBar : Control
{
    private double _progress;
    private bool _active;

    public double Progress
    {
        get => _progress;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(_progress - next) < 0.0001)
            {
                return;
            }
            _progress = next;
            Invalidate();
        }
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }
            _active = value;
            Invalidate();
        }
    }

    public SessionProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        AccessibleName = "Выполнение лимита сессии";
        AccessibleRole = AccessibleRole.ProgressBar;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var track = new SolidBrush(Theme.Border);
        e.Graphics.FillRectangle(track, ClientRectangle);
        if (_progress <= 0 || Width <= 0 || Height <= 0)
        {
            return;
        }
        using var fill = new LinearGradientBrush(ClientRectangle,
            _active ? Theme.Accent : Theme.Green,
            _active ? Theme.AccentHover : Theme.Green,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(fill, 0, 0, (int)Math.Round(Width * _progress), Height);
    }
}
