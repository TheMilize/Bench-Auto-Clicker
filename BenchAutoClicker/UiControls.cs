using System.Drawing.Drawing2D;

namespace BenchAutoClicker;

internal sealed class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 14;
    public Color BorderColor { get; set; } = Theme.Border;

    public CardPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle(ClientRectangle with
        {
            Width = Math.Max(0, ClientRectangle.Width - 1),
            Height = Math.Max(0, ClientRectangle.Height - 1)
        }, LogicalToDeviceUnits(CornerRadius));
        using var background = new SolidBrush(BackColor);
        using var border = new Pen(BorderColor);
        eventArgs.Graphics.FillPath(background, path);
        eventArgs.Graphics.DrawPath(border, path);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        Invalidate();
    }

    internal static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius) =>
        RoundedRectangle((RectangleF)rectangle, radius);

    internal static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return path;
        }
        var diameter = Math.Min(Math.Max(0, radius * 2), Math.Min(rectangle.Width, rectangle.Height));
        if (diameter == 0)
        {
            path.AddRectangle(rectangle);
            return path;
        }
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class InputSurface : Panel
{
    private readonly Control _input;
    private bool _focused;
    private bool _hovered;

    public InputSurface(Control input)
    {
        _input = input;
        DoubleBuffered = true;
        BackColor = Theme.Input;
        Padding = new Padding(9, 5, 8, 5);
        Margin = Padding.Empty;
        TabStop = false;

        input.BackColor = Theme.Input;
        input.Enter += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        input.Leave += (_, _) =>
        {
            _focused = false;
            Invalidate();
        };
        input.EnabledChanged += (_, _) => Invalidate();
        input.MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        input.MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        Controls.Add(input);
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        var availableHeight = Math.Max(1, ClientSize.Height - Padding.Vertical);
        var preferredHeight = Math.Min(availableHeight, Math.Max(LogicalToDeviceUnits(22), _input.PreferredSize.Height));
        _input.SetBounds(
            Padding.Left,
            (ClientSize.Height - preferredHeight) / 2,
            Math.Max(1, ClientSize.Width - Padding.Horizontal),
            preferredHeight);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Card);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using var path = CardPanel.RoundedRectangle(rectangle, LogicalToDeviceUnits(8));
        var inputEnabled = Enabled && _input.Enabled;
        using var fill = new SolidBrush(Theme.Input);
        using var border = new Pen(_focused && inputEnabled ? Theme.Accent : _hovered && inputEnabled ? Theme.Subtle : Theme.Border);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(border, path);
        if (_focused && inputEnabled)
        {
            using var focus = new Pen(Color.FromArgb(40, Theme.Accent));
            using var focusPath = CardPanel.RoundedRectangle(
                Rectangle.Inflate(rectangle, -1, -1),
                LogicalToDeviceUnits(7));
            eventArgs.Graphics.DrawPath(focus, focusPath);
        }
    }

}

internal sealed class FieldGrid : Panel
{
    private readonly Control[] _fields;
    private int _columns;

    public IReadOnlyList<Control> Fields => _fields;
    public int FieldCount => _fields.Length;

    public int Columns
    {
        get => _columns;
        set
        {
            var next = Math.Clamp(value, 1, Math.Max(1, _fields.Length));
            if (_columns == next)
            {
                return;
            }

            _columns = next;
            PerformLayout();
            Invalidate(true);
        }
    }

    public FieldGrid(int columns, params Control[] fields)
    {
        _fields = fields;
        _columns = Math.Clamp(columns, 1, Math.Max(1, fields.Length));
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        foreach (var field in fields)
        {
            field.Dock = DockStyle.None;
            field.Anchor = AnchorStyles.None;
            Controls.Add(field);
        }
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        if (_fields.Length == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var rows = (int)Math.Ceiling(_fields.Length / (double)Columns);
        for (var index = 0; index < _fields.Length; index++)
        {
            var column = index % Columns;
            var row = index / Columns;
            var left = (column * ClientSize.Width) / Columns;
            var right = ((column + 1) * ClientSize.Width) / Columns;
            var top = (row * ClientSize.Height) / rows;
            var bottom = ((row + 1) * ClientSize.Height) / rows;
            _fields[index].SetBounds(left, top, right - left, bottom - top);
        }
    }
}

internal sealed class PointActionsPanel : Panel
{
    private readonly Control _captureButton;
    private readonly Control _resetButton;

    public PointActionsPanel(Control captureButton, Control resetButton)
    {
        _captureButton = captureButton;
        _resetButton = resetButton;
        BackColor = Theme.Card;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        captureButton.Dock = DockStyle.None;
        resetButton.Dock = DockStyle.None;
        resetButton.Margin = Padding.Empty;
        Controls.AddRange([captureButton, resetButton]);
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        var resetWidth = Math.Min(LogicalToDeviceUnits(38), Math.Max(1, ClientSize.Width / 3));
        var gap = LogicalToDeviceUnits(5);
        _resetButton.SetBounds(
            Math.Max(0, ClientSize.Width - resetWidth),
            0,
            resetWidth,
            ClientSize.Height);
        _captureButton.SetBounds(
            0,
            0,
            Math.Max(1, _resetButton.Left - gap),
            ClientSize.Height);
    }
}

internal sealed class NumberBox : Control
{
    private readonly TextBox _editor;
    private readonly NumberStepper _stepper;
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _value;
    private int _decimalPlaces;
    private bool _focused;
    private bool _hovered;
    private bool _scrubbing;
    private bool _showProgressTrack;
    private string _zeroText = string.Empty;

    public event EventHandler? ValueChanged;

    public decimal Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Value = Math.Max(_value, _minimum);
        }
    }

    public decimal Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            Value = Math.Min(_value, _maximum);
        }
    }

    public decimal Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(
                Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero),
                Minimum,
                Maximum);
            if (_value == next)
            {
                UpdateEditorText();
                return;
            }

            _value = next;
            UpdateEditorText();
            AccessibleDescription = FormatValue();
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public decimal Increment { get; set; } = 1;
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            _decimalPlaces = Math.Clamp(value, 0, 28);
            Value = _value;
        }
    }
    public string ZeroText
    {
        get => _zeroText;
        set
        {
            _zeroText = value ?? string.Empty;
            UpdateEditorText();
        }
    }

    public bool ShowProgressTrack
    {
        get => _showProgressTrack;
        set
        {
            _showProgressTrack = value;
            PerformLayout();
            Invalidate();
        }
    }

    public bool LogarithmicTrack { get; set; }

    public NumberBox()
    {
        DoubleBuffered = true;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Cursor = Cursors.IBeam;
        TabStop = false;
        AccessibleRole = AccessibleRole.SpinButton;
        AccessibleName = AppStrings.T("Числовое значение", "Numeric value");
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _editor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Left,
            TabStop = true
        };
        _stepper = new NumberStepper(this);
        Controls.AddRange([_editor, _stepper]);

        _editor.Enter += (_, _) =>
        {
            _focused = true;
            UpdateEditorText();
            Invalidate();
        };
        _editor.Leave += (_, _) =>
        {
            CommitEditorText();
            _focused = false;
            UpdateEditorText();
            Invalidate();
        };
        _editor.KeyDown += EditorKeyDown;
        _editor.MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        _editor.MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        _editor.MouseWheel += (_, eventArgs) =>
        {
            if (!_editor.Focused)
            {
                return;
            }
            Step(eventArgs.Delta > 0 ? 1 : -1);
            if (eventArgs is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
        };
        EnabledChanged += (_, _) =>
        {
            _editor.Enabled = Enabled;
            _stepper.Enabled = Enabled;
            Invalidate();
        };
        UpdateEditorText();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(proposedSize.Width, LogicalToDeviceUnits(100)), LogicalToDeviceUnits(34));

    internal void FocusEditor()
    {
        _editor.Focus();
    }

    internal void Commit()
    {
        CommitEditorText();
    }

    internal void Step(int direction)
    {
        if (!Enabled)
        {
            return;
        }

        CommitEditorText();
        Value += direction * Increment;
        FocusEditor();
        _editor.SelectAll();
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        var edge = LogicalToDeviceUnits(7);
        var trackSpace = ShowProgressTrack ? LogicalToDeviceUnits(6) : 0;
        var contentHeight = Math.Max(1, Height - trackSpace);
        var stepperWidth = Math.Min(LogicalToDeviceUnits(56), Math.Max(24, Width / 2));
        var stepperHeight = Math.Min(LogicalToDeviceUnits(28), Math.Max(1, contentHeight - (edge * 2)));
        _stepper.SetBounds(Math.Max(0, Width - edge - stepperWidth), (contentHeight - stepperHeight) / 2, stepperWidth, stepperHeight);
        var editorHeight = Math.Min(_editor.PreferredHeight, Math.Max(1, contentHeight - LogicalToDeviceUnits(8)));
        _editor.SetBounds(
            LogicalToDeviceUnits(11),
            (contentHeight - editorHeight) / 2,
            Math.Max(1, _stepper.Left - LogicalToDeviceUnits(17)),
            editorHeight);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (Enabled && eventArgs.Button == MouseButtons.Left && ShowProgressTrack &&
            eventArgs.Y >= Height - LogicalToDeviceUnits(11))
        {
            _scrubbing = true;
            Capture = true;
            ScrubAt(eventArgs.X);
        }
        FocusEditor();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        Cursor = ShowProgressTrack && eventArgs.Y >= Height - LogicalToDeviceUnits(11)
            ? Cursors.Hand : Cursors.IBeam;
        if (_scrubbing)
        {
            ScrubAt(eventArgs.X);
        }
        base.OnMouseMove(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _scrubbing = false;
        Capture = false;
        base.OnMouseUp(eventArgs);
    }

    protected override void OnMouseCaptureChanged(EventArgs eventArgs)
    {
        _scrubbing = false;
        base.OnMouseCaptureChanged(eventArgs);
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Card);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = CardPanel.RoundedRectangle(rectangle, LogicalToDeviceUnits(9));
        using var fill = new SolidBrush(Theme.Input);
        using var border = new Pen(_focused && Enabled ? Theme.Accent : _hovered && Enabled ? Theme.Subtle : Theme.Border);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(border, path);
        if (ShowProgressTrack && Width > LogicalToDeviceUnits(28) && Height > LogicalToDeviceUnits(20))
        {
            var trackLeft = LogicalToDeviceUnits(11);
            var trackRight = Width - trackLeft;
            var trackY = Height - LogicalToDeviceUnits(6);
            var ratio = Maximum == Minimum ? 0d : LogarithmicTrack && Minimum > 0
                ? Math.Log((double)(Value / Minimum)) / Math.Log((double)(Maximum / Minimum))
                : (double)((Value - Minimum) / (Maximum - Minimum));
            var position = trackLeft + (int)Math.Round(Math.Clamp(ratio, 0, 1) * (trackRight - trackLeft));
            using var rail = new Pen(Theme.Border, Math.Max(1, LogicalToDeviceUnits(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var progress = new Pen(Enabled ? Theme.AccentDeep : Theme.Border, rail.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            eventArgs.Graphics.DrawLine(rail, trackLeft, trackY, trackRight, trackY);
            eventArgs.Graphics.DrawLine(progress, trackLeft, trackY, position, trackY);
            if (Enabled && (_focused || _hovered || _scrubbing))
            {
                var radius = LogicalToDeviceUnits(3);
                using var thumb = new SolidBrush(Theme.Accent);
                eventArgs.Graphics.FillEllipse(thumb, position - radius, trackY - radius, radius * 2, radius * 2);
            }
        }
    }

    private void ScrubAt(int x)
    {
        var edge = LogicalToDeviceUnits(11);
        var ratio = Math.Clamp((x - edge) / (double)Math.Max(1, Width - edge * 2), 0, 1);
        var value = LogarithmicTrack && Minimum > 0
            ? (decimal)((double)Minimum * Math.Pow((double)(Maximum / Minimum), ratio))
            : Minimum + (decimal)ratio * (Maximum - Minimum);
        Value = Math.Round(value, Math.Clamp(DecimalPlaces, 0, 28));
    }

    private void EditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.KeyCode)
        {
            case Keys.Enter:
                CommitEditorText();
                _editor.SelectAll();
                eventArgs.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                UpdateEditorText();
                _editor.SelectAll();
                eventArgs.SuppressKeyPress = true;
                break;
            case Keys.Up:
                Step(1);
                eventArgs.SuppressKeyPress = true;
                break;
            case Keys.Down:
                Step(-1);
                eventArgs.SuppressKeyPress = true;
                break;
        }
    }

    private void CommitEditorText()
    {
        if (NumberText.TryParse(_editor.Text, out var value))
        {
            Value = value;
        }
        UpdateEditorText();
    }

    private void UpdateEditorText()
    {
        if (_editor is null)
        {
            return;
        }

        var formatted = !_focused && _value == 0 && !string.IsNullOrWhiteSpace(ZeroText)
            ? ZeroText : FormatValue();
        if (_editor.Text != formatted)
        {
            _editor.Text = formatted;
        }
    }

    private string FormatValue() => _value.ToString($"F{DecimalPlaces}");
}

internal sealed class NumberStepper : Control
{
    private readonly NumberBox _number;
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 400 };
    private int _hoveredSide = -1;
    private int _pressedSide = -1;

    public NumberStepper(NumberBox number)
    {
        _number = number;
        BackColor = Theme.Input;
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        _repeat.Tick += (_, _) =>
        {
            if (_pressedSide < 0 || !_number.Enabled)
            {
                _repeat.Stop();
                return;
            }
            _repeat.Interval = 65;
            _number.Step(_pressedSide == 0 ? -1 : 1);
        };
        number.EnabledChanged += (_, _) =>
        {
            _repeat.Stop();
            Invalidate();
        };
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        var hovered = eventArgs.X < Width / 2 ? 0 : 1;
        if (_hoveredSide != hovered)
        {
            _hoveredSide = hovered;
            Invalidate();
        }
        base.OnMouseMove(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hoveredSide = -1;
        _pressedSide = -1;
        _repeat.Stop();
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left && _number.Enabled)
        {
            _number.FocusEditor();
            _pressedSide = eventArgs.X < Width / 2 ? 0 : 1;
            if (_pressedSide == 1)
            {
                _number.Step(1);
            }
            else
            {
                _number.Step(-1);
            }
            _repeat.Interval = 400;
            _repeat.Start();
            Invalidate();
        }
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _pressedSide = -1;
        _repeat.Stop();
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Theme.Input);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var halfWidth = Width / 2;
        var gap = LogicalToDeviceUnits(2);
        for (var side = 0; side < 2; side++)
        {
            var bounds = side == 0
                ? new Rectangle(0, 0, Math.Max(1, halfWidth - gap), Math.Max(1, Height - 1))
                : new Rectangle(halfWidth + gap, 0, Math.Max(1, Width - halfWidth - gap - 1), Math.Max(1, Height - 1));
            using var path = CardPanel.RoundedRectangle(bounds, LogicalToDeviceUnits(5));
            var color = _number.Enabled && side == _hoveredSide
                ? _pressedSide == side ? Theme.AccentSoft : Theme.CardHover
                : Theme.Sidebar;
            using var fill = new SolidBrush(color);
            eventArgs.Graphics.FillPath(fill, path);
        }

        var glyphColor = _number.Enabled ? Theme.Text : Theme.Border;
        using var glyph = new Pen(glyphColor, Math.Max(1, LogicalToDeviceUnits(1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var centerY = Height / 2;
        var glyphHalf = LogicalToDeviceUnits(3);
        var minusX = (halfWidth - gap) / 2;
        var plusX = halfWidth + gap + (Width - halfWidth - gap) / 2;
        eventArgs.Graphics.DrawLine(glyph, minusX - glyphHalf, centerY, minusX + glyphHalf, centerY);
        eventArgs.Graphics.DrawLine(glyph, plusX - glyphHalf, centerY, plusX + glyphHalf, centerY);
        eventArgs.Graphics.DrawLine(glyph, plusX, centerY - glyphHalf, plusX, centerY + glyphHalf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeat.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SelectBox : Control
{
    private readonly List<string> _items;
    private readonly ContextMenuStrip _menu;
    private int _selectedIndex;
    private bool _hovered;
    private bool _menuOpen;

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<string> Items => _items;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex == next)
            {
                return;
            }

            _selectedIndex = next;
            Text = next >= 0 ? _items[next] : string.Empty;
            AccessibleName = Text;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public SelectBox(IEnumerable<string> items)
    {
        _items = items.ToList();
        _selectedIndex = _items.Count > 0 ? 0 : -1;
        Text = _selectedIndex >= 0 ? _items[_selectedIndex] : string.Empty;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 8.8F);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.ComboBox;
        AccessibleName = Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9F),
            Padding = new Padding(4),
            Renderer = new DarkMenuRenderer()
        };
        _menu.Opened += (_, _) => { _menuOpen = true; Invalidate(); };
        _menu.Closed += (_, _) => { _menuOpen = false; Invalidate(); };
    }

    public void ReplaceItems(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacements = items.ToArray();
        var previous = _selectedIndex;
        _menu.Close();
        while (_menu.Items.Count > 0)
        {
            var item = _menu.Items[0];
            _menu.Items.RemoveAt(0);
            item.Dispose();
        }
        _items.Clear();
        _items.AddRange(replacements);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(previous, 0, _items.Count - 1);
        Text = _selectedIndex >= 0 ? _items[_selectedIndex] : string.Empty;
        AccessibleName = Text;
        if (_selectedIndex != previous)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
        Invalidate();
    }

    internal void RefreshThemeColors()
    {
        _menu.BackColor = Theme.Card;
        _menu.ForeColor = Theme.Text;
        foreach (ToolStripItem item in _menu.Items)
        {
            item.BackColor = Theme.Card;
            item.ForeColor = Theme.Text;
        }
        _menu.Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, LogicalToDeviceUnits(26));

    protected override void OnClick(EventArgs eventArgs)
    {
        base.OnClick(eventArgs);
        Focus();
        ShowMenu();
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.F4 || (eventArgs.Alt && eventArgs.KeyCode == Keys.Down))
        {
            ShowMenu();
            eventArgs.SuppressKeyPress = true;
            base.OnKeyDown(eventArgs);
            return;
        }
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                SelectedIndex--;
                eventArgs.SuppressKeyPress = true;
                break;
            case Keys.Down:
                SelectedIndex++;
                eventArgs.SuppressKeyPress = true;
                break;
            case Keys.Enter:
            case Keys.Space:
                ShowMenu();
                eventArgs.SuppressKeyPress = true;
                break;
        }
        base.OnKeyDown(eventArgs);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        if (Focused && Enabled)
        {
            SelectedIndex += eventArgs.Delta > 0 ? -1 : 1;
        }
        base.OnMouseWheel(eventArgs);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnGotFocus(eventArgs);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnLostFocus(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var arrowArea = LogicalToDeviceUnits(20);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            new Rectangle(0, 0, Math.Max(1, Width - arrowArea), Height),
            Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var buttonSize = Math.Max(1, Math.Min(Height - LogicalToDeviceUnits(2), LogicalToDeviceUnits(22)));
        var buttonRectangle = new Rectangle(
            Width - buttonSize,
            (Height - buttonSize) / 2,
            buttonSize,
            buttonSize);
        var centerX = buttonRectangle.Left + buttonRectangle.Width / 2;
        var centerY = Height / 2;
        var arrowSize = LogicalToDeviceUnits(3);
        using var arrow = new Pen(
            Enabled && (_hovered || Focused || _menuOpen) ? Theme.Accent : Theme.Subtle,
            Math.Max(1, LogicalToDeviceUnits(1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var direction = _menuOpen ? -1 : 1;
        eventArgs.Graphics.DrawLines(arrow,
        new Point[]
        {
            new Point(centerX - arrowSize, centerY - direction * arrowSize / 2),
            new Point(centerX, centerY + direction * arrowSize / 2),
            new Point(centerX + arrowSize, centerY - direction * arrowSize / 2)
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _menu.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ShowMenu()
    {
        if (!Enabled || _items.Count == 0)
        {
            return;
        }

        RefreshThemeColors();

        while (_menu.Items.Count > 0)
        {
            var oldItem = _menu.Items[0];
            _menu.Items.RemoveAt(0);
            oldItem.Dispose();
        }
        _menu.MinimumSize = new Size(Math.Max(Width, LogicalToDeviceUnits(170)), 0);
        for (var index = 0; index < _items.Count; index++)
        {
            var itemIndex = index;
            var item = new ToolStripMenuItem($"{(index == SelectedIndex ? "✓" : " ")}   {_items[index]}")
            {
                AutoSize = false,
                Height = LogicalToDeviceUnits(34),
                Width = _menu.MinimumSize.Width - _menu.Padding.Horizontal
            };
            item.Click += (_, _) => SelectedIndex = itemIndex;
            _menu.Items.Add(item);
        }
        _menu.PerformLayout();
        var preferredSize = _menu.GetPreferredSize(Size.Empty);
        var workingArea = Screen.FromControl(this).WorkingArea;
        var controlTopLeft = PointToScreen(Point.Empty);
        var menuX = Math.Clamp(
            controlTopLeft.X,
            workingArea.Left,
            Math.Max(workingArea.Left, workingArea.Right - preferredSize.Width));
        var menuY = controlTopLeft.Y + Height + LogicalToDeviceUnits(3);
        if (menuY + preferredSize.Height > workingArea.Bottom)
        {
            menuY = controlTopLeft.Y - preferredSize.Height - LogicalToDeviceUnits(3);
        }
        menuY = Math.Clamp(
            menuY,
            workingArea.Top,
            Math.Max(workingArea.Top, workingArea.Bottom - preferredSize.Height));
        _menu.Show(new Point(menuX, menuY));
    }
}

internal sealed class DarkMenuRenderer : ToolStripRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Theme.Card);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs eventArgs)
    {
        using var border = new Pen(Theme.Border);
        eventArgs.Graphics.DrawRectangle(border, 0, 0, eventArgs.ToolStrip.Width - 1, eventArgs.ToolStrip.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs eventArgs)
    {
        if (eventArgs.Item.Selected)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(2, 1, Math.Max(1, eventArgs.Item.Width - 4), Math.Max(1, eventArgs.Item.Height - 2));
            var dpiScale = (eventArgs.ToolStrip?.DeviceDpi ?? 96) / 96F;
            using var path = CardPanel.RoundedRectangle(bounds, Math.Max(1, (int)Math.Round(5 * dpiScale)));
            using var background = new SolidBrush(Theme.AccentSoft);
            eventArgs.Graphics.FillPath(background, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
    {
        eventArgs.TextColor = eventArgs.Item.Selected ? Theme.AccentText : Theme.Text;
        base.OnRenderItemText(eventArgs);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    private bool _hovered;
    private float _progress;
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };

    public ToggleSwitch()
    {
        AutoSize = false;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 8.5F);
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _animation.Tick += (_, _) =>
        {
            var target = Checked ? 1F : 0F;
            _progress += (target - _progress) * 0.35F;
            if (Math.Abs(target - _progress) < 0.02F)
            {
                _progress = target;
                _animation.Stop();
            }
            Invalidate();
        };
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(proposedSize.Width, LogicalToDeviceUnits(120)), LogicalToDeviceUnits(26));

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnCheckedChanged(EventArgs eventArgs)
    {
        if (IsHandleCreated)
        {
            _animation.Start();
        }
        else
        {
            _progress = Checked ? 1 : 0;
        }
        Invalidate();
        base.OnCheckedChanged(eventArgs);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnGotFocus(eventArgs);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnLostFocus(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(BackColor);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackWidth = LogicalToDeviceUnits(32);
        var trackHeight = LogicalToDeviceUnits(18);
        var track = new Rectangle(0, (Height - trackHeight) / 2, trackWidth, trackHeight);
        using var trackPath = CardPanel.RoundedRectangle(track, trackHeight / 2);
        var enabled = Enabled;
        var trackColor = !enabled ? Theme.Hairline : Theme.Blend(
            _hovered ? Theme.Subtle : Theme.Border,
            Theme.AccentDeep,
            _progress);
        using var trackBrush = new SolidBrush(trackColor);
        using var trackBorder = new Pen(Checked && enabled ? Theme.AccentDeep : Theme.Border);
        eventArgs.Graphics.FillPath(trackBrush, trackPath);
        eventArgs.Graphics.DrawPath(trackBorder, trackPath);

        var knobSize = trackHeight - LogicalToDeviceUnits(4);
        var knobLeft = track.Left + LogicalToDeviceUnits(2) +
            (int)Math.Round((track.Width - knobSize - LogicalToDeviceUnits(4)) * _progress);
        var knob = new Rectangle(knobLeft, track.Top + LogicalToDeviceUnits(2), knobSize, knobSize);
        using var knobBrush = new SolidBrush(enabled ? Theme.Text : Theme.Subtle);
        eventArgs.Graphics.FillEllipse(knobBrush, knob);

        var textLeft = track.Right + LogicalToDeviceUnits(7);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft), Height),
            enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Color.FromArgb(120, Theme.AccentHover));
            using var focusPath = CardPanel.RoundedRectangle(
                Rectangle.Inflate(track, LogicalToDeviceUnits(2), LogicalToDeviceUnits(2)),
                trackHeight / 2 + LogicalToDeviceUnits(2));
            eventArgs.Graphics.DrawPath(focus, focusPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class LogoBadge : Control
{
    private readonly Image _image;

    public LogoBadge(Image image)
    {
        _image = image;
        DoubleBuffered = true;
        Size = new Size(38, 38);
        AccessibleName = "Bench Auto Clicker";
        AccessibleRole = AccessibleRole.Graphic;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var rectangle = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        var background = Parent?.BackColor ?? Theme.Window;
        if (Theme.ContrastRatio(Color.White, background) < 3)
        {
            // Keep the mark white in every app theme. A fine neutral edge makes it
            // readable on a light surface without adding a background tile.
            using var edge = new System.Drawing.Imaging.ImageAttributes();
            edge.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0.65F, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            }));
            foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
            {
                var outline = rectangle;
                outline.Offset(offset);
                eventArgs.Graphics.DrawImage(_image, outline, 0, 0, _image.Width, _image.Height,
                    GraphicsUnit.Pixel, edge);
            }
        }
        eventArgs.Graphics.DrawImage(_image, rectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _image.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal class RoundedButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _selected;

    public Color FillColor { get; set; } = Theme.CardHover;
    public Color HoverColor { get; set; } = Theme.AccentSoft;
    public Color SelectedColor { get; set; } = Theme.AccentSoft;
    public Color BorderColor { get; set; } = Theme.Border;
    public bool AccentStripWhenSelected { get; set; }
    public bool BottomAccentWhenSelected { get; set; }
    public string IconGlyph { get; set; } = string.Empty;
    protected bool IsHovered => _hovered;
    protected bool IsPressed => _pressed;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            AccessibleDescription = value ? AppStrings.T("Выбрано", "Selected") : null;
            Invalidate();
        }
    }
    public int CornerRadius { get; set; } = 10;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        TextAlign = ContentAlignment.MiddleCenter;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        // ButtonBase enables Opaque. Rounded buttons leave their corners unpainted,
        // so WM_PAINT must repaint the parent background before every state change.
        SetStyle(ControlStyles.Opaque, false);
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnKeyDown(eventArgs);
    }

    protected override void OnKeyUp(KeyEventArgs eventArgs)
    {
        _pressed = false;
        Invalidate();
        base.OnKeyUp(eventArgs);
    }

    protected override void OnGotFocus(EventArgs eventArgs)
    {
        Invalidate();
        base.OnGotFocus(eventArgs);
    }

    protected override void OnLostFocus(EventArgs eventArgs)
    {
        _pressed = false;
        Invalidate();
        base.OnLostFocus(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        _pressed = false;
        _hovered = false;
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = !Enabled
            ? Theme.Sidebar
            : Selected
                ? SelectedColor
                : _hovered
                    ? HoverColor
                    : FillColor;
        var drawSurface = !AccentStripWhenSelected || Selected || _hovered;
        if (drawSurface)
        {
            // Keep the full anti-aliased 1px stroke inside the control's pixel bounds.
            var surfaceRectangle = new RectangleF(0.5F, 0.5F, Math.Max(0, Width - 1F), Math.Max(0, Height - 1F));
            using var path = CardPanel.RoundedRectangle(surfaceRectangle, LogicalToDeviceUnits(CornerRadius));
            using var brush = new SolidBrush(fill);
            using var border = new Pen(Selected ? Theme.AccentDeep : _hovered && Enabled ? Theme.Subtle : BorderColor);
            eventArgs.Graphics.FillPath(brush, path);
            if (_pressed && Enabled)
            {
                using var pressedOverlay = new SolidBrush(Color.FromArgb(32, Color.Black));
                eventArgs.Graphics.FillPath(pressedOverlay, path);
            }
            eventArgs.Graphics.DrawPath(border, path);
        }
        if (Selected && AccentStripWhenSelected)
        {
            var stripWidth = LogicalToDeviceUnits(3);
            var stripHeight = LogicalToDeviceUnits(20);
            var stripLeft = LogicalToDeviceUnits(6);
            var stripTop = (Height - stripHeight) / 2;
            using var accent = new SolidBrush(Theme.AccentHover);
            eventArgs.Graphics.FillRectangle(accent, stripLeft, stripTop, stripWidth, stripHeight);
        }
        if (Selected && BottomAccentWhenSelected)
        {
            var accentWidth = Math.Min(Width - LogicalToDeviceUnits(18), LogicalToDeviceUnits(48));
            var accentHeight = LogicalToDeviceUnits(2);
            var accentRectangle = new Rectangle(
                (Width - accentWidth) / 2,
                Height - accentHeight - 1,
                accentWidth,
                accentHeight);
            using var accentPath = CardPanel.RoundedRectangle(accentRectangle, accentHeight / 2);
            using var accent = new SolidBrush(Theme.AccentHover);
            eventArgs.Graphics.FillPath(accent, accentPath);
        }

        var textColor = Enabled ? ForeColor : Theme.Muted;
        var textRectangle = Rectangle.FromLTRB(
            rectangle.Left + Padding.Left,
            rectangle.Top + Padding.Top,
            rectangle.Right - Padding.Right,
            rectangle.Bottom - Padding.Bottom);
        DrawContent(eventArgs.Graphics, textRectangle, textColor, fill);

        if (Focused && ShowFocusCues)
        {
            var focusRectangle = Rectangle.Inflate(rectangle, -LogicalToDeviceUnits(3), -LogicalToDeviceUnits(3));
            using var focusPath = CardPanel.RoundedRectangle(focusRectangle, LogicalToDeviceUnits(Math.Max(1, CornerRadius - 3)));
            using var focus = new Pen(Color.FromArgb(160, Selected ? Theme.Accent : textColor));
            eventArgs.Graphics.DrawPath(focus, focusPath);
        }
    }

    protected virtual void DrawContent(Graphics graphics, Rectangle textRectangle, Color textColor, Color surfaceColor)
    {
        if (!string.IsNullOrWhiteSpace(IconGlyph) && textRectangle.Width > LogicalToDeviceUnits(76))
        {
            var iconWidth = LogicalToDeviceUnits(17);
            var iconGap = LogicalToDeviceUnits(7);
            var textWidth = Math.Min(textRectangle.Width - iconWidth - iconGap,
                TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
            var groupLeft = textRectangle.Left + Math.Max(0, (textRectangle.Width - textWidth - iconWidth - iconGap) / 2);
            using var glyphFont = new Font("Segoe MDL2 Assets", 11F);
            TextRenderer.DrawText(graphics, IconGlyph, glyphFont,
                new Rectangle(groupLeft, textRectangle.Top, iconWidth, textRectangle.Height),
                Enabled ? Selected ? Theme.AccentText : Theme.Muted : Theme.Subtle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            textRectangle = new Rectangle(groupLeft + iconWidth + iconGap, textRectangle.Top, textWidth, textRectangle.Height);
        }
        var horizontalAlignment = TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textRectangle,
            textColor,
            horizontalAlignment | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

}

internal sealed class PrimaryButton : RoundedButton
{
    public bool IsStopState { get; set; }
    public string HotKeyHint { get; set; } = "F6";

    public PrimaryButton()
    {
        FillColor = Theme.Accent;
        HoverColor = Theme.AccentHover;
        SelectedColor = Theme.Accent;
        BorderColor = Theme.Accent;
        CornerRadius = 11;
        Font = new Font("Segoe UI", 9.4F, FontStyle.Bold);
        ForeColor = Theme.OnAccent;
    }

    public void ApplyState(bool stopState)
    {
        IsStopState = stopState;
        Text = stopState ? AppStrings.T("Остановить", "Stop") : AppStrings.T("Запустить", "Start");
        RefreshThemeColors();
    }

    internal void RefreshThemeColors()
    {
        FillColor = IsStopState ? Theme.Red : Theme.Accent;
        HoverColor = IsStopState ? Theme.Blend(Theme.Red, Theme.Text, 0.12F) : Theme.AccentHover;
        SelectedColor = Theme.Accent;
        ForeColor = Theme.ContrastText(FillColor);
        BorderColor = FillColor;
        Invalidate();
    }

    protected override void DrawContent(Graphics graphics, Rectangle textRectangle, Color textColor, Color surfaceColor)
    {
        textColor = Theme.ContrastText(surfaceColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96F;
        var iconSize = Math.Max(8, (int)Math.Round(9 * scale));
        var iconLeft = textRectangle.Left + LogicalToDeviceUnits(9);
        var iconTop = textRectangle.Top + (textRectangle.Height - iconSize) / 2;
        using var iconBrush = new SolidBrush(Color.FromArgb(235, textColor));
        if (IsStopState)
        {
            var stopSize = Math.Max(6, iconSize - LogicalToDeviceUnits(2));
            graphics.FillRectangle(
                iconBrush,
                iconLeft + (iconSize - stopSize) / 2,
                iconTop + (iconSize - stopSize) / 2,
                stopSize,
                stopSize);
        }
        else
        {
            graphics.FillPolygon(
                iconBrush,
                new Point[]
                {
                    new(iconLeft + LogicalToDeviceUnits(1), iconTop),
                    new(iconLeft + iconSize, iconTop + iconSize / 2),
                    new(iconLeft + LogicalToDeviceUnits(1), iconTop + iconSize)
                });
        }

        using var badgeFont = new Font("Segoe UI", 7F, FontStyle.Bold);
        var naturalBadgeWidth = Math.Max(LogicalToDeviceUnits(28), TextRenderer.MeasureText(
            graphics, HotKeyHint, badgeFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + LogicalToDeviceUnits(10));
        var labelWidth = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var labelLeft = iconLeft + iconSize + LogicalToDeviceUnits(6);
        var hasHint = !string.IsNullOrWhiteSpace(HotKeyHint) &&
            naturalBadgeWidth + labelWidth + LogicalToDeviceUnits(16) <= textRectangle.Right - labelLeft;
        var badgeWidth = hasHint ? naturalBadgeWidth : 0;
        var badgeRight = textRectangle.Right - LogicalToDeviceUnits(7);
        var badgeLeft = badgeRight - badgeWidth;
        var labelRight = hasHint ? badgeLeft - LogicalToDeviceUnits(5) : textRectangle.Right - LogicalToDeviceUnits(7);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            Rectangle.FromLTRB(labelLeft, textRectangle.Top, labelRight, textRectangle.Bottom),
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (!hasHint)
        {
            return;
        }

        var badgeHeight = Math.Min(textRectangle.Height - LogicalToDeviceUnits(8), LogicalToDeviceUnits(20));
        var badgeRectangle = new Rectangle(
            badgeLeft,
            textRectangle.Top + (textRectangle.Height - badgeHeight) / 2,
            badgeWidth,
            badgeHeight);
        using var badgePath = CardPanel.RoundedRectangle(badgeRectangle, LogicalToDeviceUnits(6));
        using var badgeFill = new SolidBrush(Color.FromArgb(IsPressed ? 24 : 15, Theme.Window));
        using var badgeBorder = new Pen(Color.FromArgb(40, Theme.Window));
        graphics.FillPath(badgeFill, badgePath);
        graphics.DrawPath(badgeBorder, badgePath);
        TextRenderer.DrawText(
            graphics,
            HotKeyHint,
            badgeFont,
            badgeRectangle,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal enum AppRunState
{
    Stopped,
    Countdown,
    Running
}

internal sealed class StatusPill : Control
{
    private AppRunState _state;

    public AppRunState State
    {
        get => _state;
        set
        {
            _state = value;
            Text = value switch
            {
                AppRunState.Running => AppStrings.T("РАБОТАЕТ", "RUNNING"),
                AppRunState.Countdown => AppStrings.T("ОТСЧЁТ", "COUNTDOWN"),
                _ => AppStrings.T("ГОТОВ", "READY")
            };
            AccessibleName = value switch
            {
                AppRunState.Running => AppStrings.T("Статус: работает", "Status: running"),
                AppRunState.Countdown => AppStrings.T("Статус: подготовка к запуску", "Status: counting down"),
                _ => AppStrings.T("Статус: остановлен", "Status: stopped")
            };
            Invalidate();
        }
    }

    public StatusPill()
    {
        DoubleBuffered = true;
        Size = new Size(92, 26);
        Font = new Font("Segoe UI", 7.6F, FontStyle.Bold);
        AccessibleRole = AccessibleRole.StaticText;
        State = AppRunState.Stopped;
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(LogicalToDeviceUnits(92), LogicalToDeviceUnits(26));

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _state switch
        {
            AppRunState.Running => Theme.Red,
            AppRunState.Countdown => Theme.Amber,
            _ => Theme.Muted
        };
        var caption = Text;
        using var path = CardPanel.RoundedRectangle(
            new Rectangle(0, 0, Width - 1, Height - 1),
            Math.Max(1, (Height - 1) / 2));
        using var background = new SolidBrush(Theme.Blend(Theme.Sidebar, color, 0.07F));
        using var outline = new Pen(Theme.Blend(Theme.Border, color, 0.25F));
        using var dot = new SolidBrush(color);
        eventArgs.Graphics.FillPath(background, path);
        eventArgs.Graphics.DrawPath(outline, path);
        var dotSize = LogicalToDeviceUnits(7);
        var dotLeft = LogicalToDeviceUnits(11);
        var textLeft = LogicalToDeviceUnits(25);
        var textRightPadding = LogicalToDeviceUnits(5);
        if (_state != AppRunState.Stopped)
        {
            var haloSize = dotSize + LogicalToDeviceUnits(5);
            using var halo = new SolidBrush(Color.FromArgb(30, color));
            eventArgs.Graphics.FillEllipse(halo,
                dotLeft - (haloSize - dotSize) / 2,
                (Height - haloSize) / 2,
                haloSize, haloSize);
        }
        eventArgs.Graphics.FillEllipse(dot, dotLeft, (Height - dotSize) / 2, dotSize, dotSize);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            caption,
            Font,
            new Rectangle(textLeft, 0, Width - textLeft - textRightPadding, Height),
            color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
    }
}
