using System.Drawing.Drawing2D;

namespace BenchAutoClicker;

/// <summary>A shortcut recorder that consumes its keys instead of typing into the application.</summary>
internal sealed class HotkeyBox : Control
{
    private string _value = string.Empty;
    private string _preview = string.Empty;
    private bool _hovered;
    private string _captureError = string.Empty;

    public event EventHandler? ValueChanged;
    public event EventHandler? CaptureStateChanged;

    public string Value
    {
        get => _value;
        set
        {
            var next = HotkeyGesture.TryParse(value, out var gesture) ? gesture.ToString() : value ?? string.Empty;
            if (_value == next) return;
            _value = next;
            _captureError = string.Empty;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsCapturing { get; private set; }
    public string CaptureError => _captureError;

    public HotkeyBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size(200, 40);
        AccessibleRole = AccessibleRole.HotkeyField;
        AccessibleDescription = AppStrings.T("Нажмите и введите сочетание клавиш. Escape отменяет ввод.",
            "Click and press a shortcut. Escape cancels recording.");
    }

    public void BeginCapture()
    {
        if (!Enabled || IsCapturing) return;
        IsCapturing = true;
        _preview = string.Empty;
        _captureError = string.Empty;
        Invalidate();
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelCapture()
    {
        if (!IsCapturing) return;
        IsCapturing = false;
        _preview = string.Empty;
        _captureError = string.Empty;
        Invalidate();
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        BeginCapture();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        BeginCapture();
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        CancelCapture();
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (IsCapturing)
        {
            // Tab remains ordinary focus navigation; Escape only cancels the recorder.
            if ((keyData & Keys.KeyCode) == Keys.Tab)
            {
                CancelCapture();
                return base.ProcessCmdKey(ref msg, keyData);
            }
            CaptureGesture(keyData);
            return true;
        }
        if (keyData == Keys.Space || keyData == Keys.Enter)
        {
            BeginCapture();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool IsInputKey(Keys keyData) => IsCapturing || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsCapturing)
        {
            CaptureGesture(e.KeyData);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            BeginCapture();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (IsCapturing && IsModifier(e.KeyCode))
        {
            SetModifierPreview(e.Modifiers);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyUp(e);
    }

    private void CaptureGesture(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) { CancelCapture(); return; }
        if (IsModifier(key)) { SetModifierPreview(keyData); return; }
        if (!HotkeyGesture.TryFromKeys(keyData, out var gesture, out var error))
        {
            _captureError = error;
            _preview = AppStrings.T("Нужны Ctrl / Alt или F-клавиша", "Use Ctrl / Alt or an F-key");
            Invalidate();
            CaptureStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        Value = gesture.ToString();
        CancelCapture();
    }

    private void SetModifierPreview(Keys keyData)
    {
        var parts = new List<string>(4);
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        parts.Add("…");
        _preview = string.Join(" + ", parts);
        _captureError = string.Empty;
        Invalidate();
    }

    private static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu;

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Theme.Card);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var path = CardPanel.RoundedRectangle(rectangle, LogicalToDeviceUnits(8));
        using var fill = new SolidBrush(Theme.Input);
        using var border = new Pen(IsCapturing || Focused ? Theme.Accent : _hovered ? Theme.Subtle : Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var text = IsCapturing ? (_preview.Length > 0 ? _preview : AppStrings.T("Нажмите сочетание…", "Press a shortcut…")) : Value;
        var textRectangle = Rectangle.Inflate(ClientRectangle, -LogicalToDeviceUnits(12), 0);
        textRectangle.Width = Math.Max(1, textRectangle.Width - LogicalToDeviceUnits(22));
        TextRenderer.DrawText(e.Graphics, text, Font, textRectangle,
            Enabled ? IsCapturing ? Theme.AccentText : Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var iconFont = new Font("Segoe MDL2 Assets", 10F);
        TextRenderer.DrawText(e.Graphics, "\uE765", iconFont,
            new Rectangle(Math.Max(0, Width - LogicalToDeviceUnits(32)), 0, LogicalToDeviceUnits(20), Height),
            IsCapturing ? Theme.AccentText : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }
}
