using System.Drawing.Drawing2D;

namespace BenchAutoClicker;

internal static class Theme
{
    public static readonly Color Window = Color.FromArgb(14, 16, 23);
    public static readonly Color Sidebar = Color.FromArgb(18, 21, 30);
    public static readonly Color Card = Color.FromArgb(24, 28, 39);
    public static readonly Color CardHover = Color.FromArgb(31, 36, 49);
    public static readonly Color Input = Color.FromArgb(17, 20, 29);
    public static readonly Color Border = Color.FromArgb(47, 53, 69);
    public static readonly Color Text = Color.FromArgb(242, 244, 249);
    public static readonly Color Muted = Color.FromArgb(150, 157, 176);
    public static readonly Color Accent = Color.FromArgb(114, 92, 255);
    public static readonly Color AccentHover = Color.FromArgb(130, 110, 255);
    public static readonly Color AccentSoft = Color.FromArgb(44, 39, 78);
    public static readonly Color Green = Color.FromArgb(70, 214, 145);
    public static readonly Color Red = Color.FromArgb(255, 91, 111);
    public static readonly Color Amber = Color.FromArgb(255, 184, 77);
}

internal sealed class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 12;
    public Color BorderColor { get; set; } = Theme.Border;

    public CardPanel()
    {
        DoubleBuffered = true;
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

    internal static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
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
        Controls.Add(input);
    }

    protected override void OnLayout(LayoutEventArgs eventArgs)
    {
        base.OnLayout(eventArgs);
        var availableHeight = Math.Max(1, ClientSize.Height - Padding.Vertical);
        var preferredHeight = Math.Min(availableHeight, Math.Max(22, _input.PreferredSize.Height));
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
        using var fill = new LinearGradientBrush(
            rectangle,
            inputEnabled ? Color.FromArgb(20, 23, 33) : Color.FromArgb(22, 25, 34),
            inputEnabled ? Theme.Input : Color.FromArgb(20, 23, 31),
            LinearGradientMode.Vertical);
        using var border = new Pen(_focused && inputEnabled ? Theme.AccentHover : Theme.Border);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(border, path);
        if (_focused && inputEnabled)
        {
            using var focus = new Pen(Color.FromArgb(55, Theme.AccentHover));
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
    private bool _focused;

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
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next)
            {
                UpdateEditorText();
                return;
            }

            _value = next;
            UpdateEditorText();
            AccessibleName = FormatValue();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public decimal Increment { get; set; } = 1;
    public int DecimalPlaces { get; set; }

    public NumberBox()
    {
        DoubleBuffered = true;
        BackColor = Theme.Input;
        ForeColor = Theme.Text;
        Cursor = Cursors.IBeam;
        TabStop = false;
        AccessibleRole = AccessibleRole.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _editor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.4F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Left,
            TabStop = true
        };
        _stepper = new NumberStepper(this);
        Controls.AddRange([_editor, _stepper]);

        _editor.Enter += (_, _) =>
        {
            _focused = true;
            Invalidate();
        };
        _editor.Leave += (_, _) =>
        {
            CommitEditorText();
            _focused = false;
            Invalidate();
        };
        _editor.KeyDown += EditorKeyDown;
        _editor.MouseWheel += (_, eventArgs) =>
        {
            Step(eventArgs.Delta > 0 ? 1 : -1);
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
        var stepperWidth = LogicalToDeviceUnits(58);
        var stepperHeight = Math.Max(1, Height - (edge * 2));
        _stepper.SetBounds(Width - edge - stepperWidth, edge, stepperWidth, stepperHeight);
        var editorHeight = Math.Min(_editor.PreferredHeight, Math.Max(1, Height - LogicalToDeviceUnits(8)));
        _editor.SetBounds(
            LogicalToDeviceUnits(11),
            (Height - editorHeight) / 2,
            Math.Max(1, _stepper.Left - LogicalToDeviceUnits(17)),
            editorHeight);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        FocusEditor();
        base.OnMouseDown(eventArgs);
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
        using var fill = new SolidBrush(Enabled ? Theme.Input : Color.FromArgb(22, 25, 34));
        using var border = new Pen(_focused && Enabled ? Theme.AccentHover : Theme.Border);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(border, path);
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

        var formatted = FormatValue();
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
        number.EnabledChanged += (_, _) => Invalidate();
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
            Invalidate();
        }
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _pressedSide = -1;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Theme.Input);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = CardPanel.RoundedRectangle(rectangle, LogicalToDeviceUnits(7));
        using var surface = new SolidBrush(Color.FromArgb(27, 31, 43));
        using var outline = new Pen(Color.FromArgb(57, 64, 82));
        eventArgs.Graphics.FillPath(surface, path);
        eventArgs.Graphics.DrawPath(outline, path);

        var halfWidth = Width / 2;
        if (_hoveredSide >= 0 && _number.Enabled)
        {
            var hoverRectangle = _hoveredSide == 0
                ? new Rectangle(1, 1, halfWidth - 1, Height - 2)
                : new Rectangle(halfWidth, 1, Width - halfWidth - 1, Height - 2);
            using var hoverPath = CardPanel.RoundedRectangle(hoverRectangle, LogicalToDeviceUnits(6));
            using var hover = new SolidBrush(_pressedSide == _hoveredSide
                ? Color.FromArgb(50, 45, 88)
                : Color.FromArgb(39, 44, 59));
            eventArgs.Graphics.FillPath(hover, hoverPath);
        }

        using var separator = new Pen(Theme.Border);
        eventArgs.Graphics.DrawLine(
            separator,
            halfWidth,
            LogicalToDeviceUnits(5),
            halfWidth,
            Height - LogicalToDeviceUnits(5));

        var glyphColor = _number.Enabled ? Theme.Text : Theme.Border;
        using var glyph = new Pen(glyphColor, Math.Max(1, LogicalToDeviceUnits(1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var centerY = Height / 2;
        var glyphHalf = LogicalToDeviceUnits(3);
        var minusX = halfWidth / 2;
        var plusX = halfWidth + (Width - halfWidth) / 2;
        eventArgs.Graphics.DrawLine(glyph, minusX - glyphHalf, centerY, minusX + glyphHalf, centerY);
        eventArgs.Graphics.DrawLine(glyph, plusX - glyphHalf, centerY, plusX + glyphHalf, centerY);
        eventArgs.Graphics.DrawLine(glyph, plusX, centerY - glyphHalf, plusX, centerY + glyphHalf);
    }
}

internal sealed class SelectBox : Control
{
    private readonly List<string> _items;
    private readonly ContextMenuStrip _menu;
    private int _selectedIndex;
    private bool _hovered;

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
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                SelectedIndex--;
                eventArgs.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex++;
                eventArgs.Handled = true;
                break;
            case Keys.Enter:
            case Keys.Space:
                ShowMenu();
                eventArgs.Handled = true;
                break;
        }
        base.OnKeyDown(eventArgs);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        SelectedIndex += eventArgs.Delta > 0 ? -1 : 1;
        base.OnMouseWheel(eventArgs);
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

        var buttonSize = Math.Min(Height - LogicalToDeviceUnits(2), LogicalToDeviceUnits(22));
        var buttonRectangle = new Rectangle(
            Width - buttonSize,
            (Height - buttonSize) / 2,
            buttonSize,
            buttonSize);
        using var buttonPath = CardPanel.RoundedRectangle(buttonRectangle, LogicalToDeviceUnits(6));
        using var buttonFill = new SolidBrush(_hovered || Focused
            ? Color.FromArgb(41, 46, 62)
            : Color.FromArgb(28, 32, 44));
        eventArgs.Graphics.FillPath(buttonFill, buttonPath);

        var centerX = buttonRectangle.Left + buttonRectangle.Width / 2;
        var centerY = Height / 2;
        var arrowSize = LogicalToDeviceUnits(3);
        using var arrow = new Pen(
            Enabled && (_hovered || Focused) ? Theme.Text : Theme.Muted,
            Math.Max(1, LogicalToDeviceUnits(1)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        eventArgs.Graphics.DrawLines(arrow,
        new Point[]
        {
            new Point(centerX - arrowSize, centerY - arrowSize / 2),
            new Point(centerX, centerY + arrowSize / 2),
            new Point(centerX + arrowSize, centerY - arrowSize / 2)
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
            var item = new ToolStripMenuItem($"{(index == SelectedIndex ? "●" : " ")}   {_items[index]}")
            {
                AutoSize = false,
                Height = LogicalToDeviceUnits(30),
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
        var bounds = new Rectangle(Point.Empty, eventArgs.Item.Size);
        using var background = new SolidBrush(eventArgs.Item.Selected ? Theme.AccentSoft : Theme.Card);
        eventArgs.Graphics.FillRectangle(background, bounds);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
    {
        eventArgs.TextColor = eventArgs.Item.Selected ? Theme.Text : Theme.Muted;
        base.OnRenderItemText(eventArgs);
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    private bool _hovered;

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
        var trackColor = !enabled
            ? Color.FromArgb(32, 36, 47)
            : Checked
                ? Theme.Accent
                : _hovered
                    ? Color.FromArgb(56, 62, 79)
                    : Color.FromArgb(43, 48, 63);
        using var trackBrush = new LinearGradientBrush(
            track,
            Lighten(trackColor, 10),
            Darken(trackColor, 8),
            LinearGradientMode.Vertical);
        using var trackBorder = new Pen(Checked && enabled ? Theme.AccentHover : Theme.Border);
        eventArgs.Graphics.FillPath(trackBrush, trackPath);
        eventArgs.Graphics.DrawPath(trackBorder, trackPath);

        var knobSize = trackHeight - LogicalToDeviceUnits(4);
        var knobLeft = Checked
            ? track.Right - knobSize - LogicalToDeviceUnits(2)
            : track.Left + LogicalToDeviceUnits(2);
        var knob = new Rectangle(knobLeft, track.Top + LogicalToDeviceUnits(2), knobSize, knobSize);
        using var knobShadow = new SolidBrush(Color.FromArgb(60, Color.Black));
        eventArgs.Graphics.FillEllipse(knobShadow, knob.X, knob.Y + 1, knob.Width, knob.Height);
        using var knobBrush = new LinearGradientBrush(
            knob,
            enabled ? Color.White : Theme.Muted,
            enabled ? Color.FromArgb(207, 212, 226) : Theme.Border,
            LinearGradientMode.Vertical);
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

    private static Color Lighten(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount));

    private static Color Darken(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Max(0, color.R - amount),
        Math.Max(0, color.G - amount),
        Math.Max(0, color.B - amount));
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
        var rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
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
            AccessibleDescription = value ? "Выбрано" : null;
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

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = !Enabled
            ? Color.FromArgb(31, 34, 43)
            : Selected
                ? SelectedColor
                : _hovered
                    ? HoverColor
                    : FillColor;
        var drawSurface = !AccentStripWhenSelected || Selected || _hovered;
        if (drawSurface)
        {
            using var path = CardPanel.RoundedRectangle(rectangle, LogicalToDeviceUnits(CornerRadius));
            using var brush = new SolidBrush(fill);
            using var border = new Pen(Selected ? Theme.AccentHover : BorderColor);
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
            var focusRectangle = Rectangle.Inflate(rectangle, -LogicalToDeviceUnits(4), -LogicalToDeviceUnits(4));
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focusRectangle, textColor, fill);
        }
    }

    protected virtual void DrawContent(Graphics graphics, Rectangle textRectangle, Color textColor, Color surfaceColor)
    {
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
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
    }

    public void ApplyState(bool stopState)
    {
        IsStopState = stopState;
        Text = stopState ? "Остановить" : "Запустить";
        FillColor = stopState ? Theme.Red : Theme.Accent;
        HoverColor = stopState ? Color.FromArgb(255, 112, 129) : Theme.AccentHover;
        BorderColor = FillColor;
        Invalidate();
    }

    protected override void DrawContent(Graphics graphics, Rectangle textRectangle, Color textColor, Color surfaceColor)
    {
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

        var hasHint = !string.IsNullOrWhiteSpace(HotKeyHint);
        var badgeWidth = hasHint ? LogicalToDeviceUnits(28) : 0;
        var badgeRight = textRectangle.Right - LogicalToDeviceUnits(7);
        var badgeLeft = badgeRight - badgeWidth;
        var labelLeft = iconLeft + iconSize + LogicalToDeviceUnits(6);
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
        using var badgeFill = new SolidBrush(Color.FromArgb(IsPressed ? 32 : 45, Color.White));
        using var badgeBorder = new Pen(Color.FromArgb(55, Color.White));
        graphics.FillPath(badgeFill, badgePath);
        graphics.DrawPath(badgeBorder, badgePath);
        using var badgeFont = new Font("Segoe UI", 7F, FontStyle.Bold);
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
            AccessibleName = value switch
            {
                AppRunState.Running => "Статус: работает",
                AppRunState.Countdown => "Статус: подготовка к запуску",
                _ => "Статус: остановлен"
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

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _state switch
        {
            AppRunState.Running => Theme.Red,
            AppRunState.Countdown => Theme.Amber,
            _ => Theme.Muted
        };
        var caption = _state switch
        {
            AppRunState.Running => "РАБОТАЕТ",
            AppRunState.Countdown => "ГОТОВ",
            _ => "СТОП"
        };
        using var path = CardPanel.RoundedRectangle(
            new Rectangle(0, 0, Width - 1, Height - 1),
            Math.Max(1, (Height - 1) / 2));
        using var background = new SolidBrush(Color.FromArgb(28, color));
        using var outline = new Pen(Color.FromArgb(90, color));
        using var dot = new SolidBrush(color);
        eventArgs.Graphics.FillPath(background, path);
        eventArgs.Graphics.DrawPath(outline, path);
        var dotSize = LogicalToDeviceUnits(7);
        var dotLeft = LogicalToDeviceUnits(11);
        var textLeft = LogicalToDeviceUnits(25);
        var textRightPadding = LogicalToDeviceUnits(5);
        eventArgs.Graphics.FillEllipse(dot, dotLeft, (Height - dotSize) / 2, dotSize, dotSize);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            caption,
            Font,
            new Rectangle(textLeft, 0, Width - textLeft - textRightPadding, Height),
            color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}
