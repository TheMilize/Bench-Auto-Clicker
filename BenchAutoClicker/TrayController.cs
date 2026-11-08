namespace BenchAutoClicker;

internal sealed record TraySnapshot(ClickMode Mode, bool IsRunning, bool IsCountingDown, long Clicks, string StatusText,
    bool GlobalToggleAvailable = true, string EmergencyHotKey = "Ctrl+Shift+F6", HotkeySettings? Hotkeys = null)
{
    public bool IsBusy => IsRunning || IsCountingDown;
    public HotkeySettings Shortcuts => Hotkeys ?? HotkeySettings.Defaults;
}

internal sealed class TrayController : IDisposable
{
    private readonly Func<TraySnapshot> _snapshot;
    private readonly Action _toggle;
    private readonly Action<ClickMode> _selectMode;
    private readonly Action _settings;
    private readonly Action _exit;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _contextMenu;
    private TrayMenuForm? _flyout;
    private Icon? _trayIcon;
    private bool _disposed;
    private long _lastDismissedAt;

    public bool Visible => _icon.Visible;
    public bool IsVisible => _icon.Visible;
    internal TrayMenuForm? Flyout => _flyout;

    public TrayController(Func<TraySnapshot> snapshot, Action toggle, Action<ClickMode> selectMode,
        Action settings, Action exit)
    {
        _snapshot = snapshot;
        _toggle = toggle;
        _selectMode = selectMode;
        _settings = settings;
        _exit = exit;
        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9F),
            Renderer = new DarkMenuRenderer(),
            Padding = new Padding(4)
        };
        _contextMenu.Opening += (_, _) => BuildContextMenu();
        _icon = new NotifyIcon
        {
            Text = "Bench Auto Clicker",
            ContextMenuStrip = _contextMenu,
            Visible = false
        };
        _icon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                ShowMenu();
            }
        };
        _icon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                HideMenu();
                _settings();
            }
        };
        RefreshAppearance();
    }

    public void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }
        _icon.Visible = visible;
        if (!visible)
        {
            HideMenu();
        }
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }
        var snapshot = _snapshot();
        var title = $"Bench Auto Clicker · {snapshot.StatusText}";
        _icon.Text = title.Length <= 63 ? title : title[..63];
        if (_flyout is { IsDisposed: false, Visible: true })
        {
            _flyout.UpdateSnapshot(snapshot);
        }
    }

    public void RefreshAppearance()
    {
        if (_disposed)
        {
            return;
        }
        var oldIcon = _trayIcon;
        _trayIcon = BrandAssets.LoadTrayIcon(Theme.IsTaskbarDark);
        _icon.Icon = _trayIcon;
        oldIcon?.Dispose();
        _contextMenu.BackColor = Theme.Card;
        _contextMenu.ForeColor = Theme.Text;
        if (_flyout is { IsDisposed: false })
        {
            _flyout.RefreshAppearance();
        }
        Refresh();
    }

    public void ShowMenu()
    {
        if (_disposed || !_icon.Visible || Environment.TickCount64 - _lastDismissedAt < 180)
        {
            return;
        }
        if (_flyout is { IsDisposed: false, Visible: true })
        {
            HideMenu();
            return;
        }
        if (_flyout is null || _flyout.IsDisposed)
        {
            _flyout = new TrayMenuForm(_toggle, _selectMode,
                () => { HideMenu(); _settings(); },
                () => { HideMenu(); _exit(); });
            _flyout.Deactivate += (_, _) => _lastDismissedAt = Environment.TickCount64;
        }
        _flyout.RefreshAppearance();
        _flyout.UpdateSnapshot(_snapshot());
        _flyout.ShowNear(Control.MousePosition);
    }

    public void HideMenu()
    {
        if (_flyout is { IsDisposed: false })
        {
            _flyout.Hide();
        }
    }

    private void BuildContextMenu()
    {
        while (_contextMenu.Items.Count > 0)
        {
            var oldItem = _contextMenu.Items[0];
            _contextMenu.Items.RemoveAt(0);
            oldItem.Dispose();
        }
        var snapshot = _snapshot();
        var toggle = new ToolStripMenuItem(snapshot.IsBusy
            ? AppStrings.T("Остановить", "Stop") : AppStrings.T("Запустить", "Start"))
        { ShortcutKeyDisplayString = snapshot.Shortcuts.Toggle };
        toggle.Click += (_, _) => _toggle();
        _contextMenu.Items.Add(toggle);
        var modes = new ToolStripMenuItem(AppStrings.T("Режим", "Mode"));
        foreach (var mode in Enum.GetValues<ClickMode>())
        {
            var item = new ToolStripMenuItem(ModeCaption(mode))
            {
                Checked = mode == snapshot.Mode,
                Enabled = !snapshot.IsBusy,
                ShortcutKeyDisplayString = mode switch
                {
                    ClickMode.RandomInterval => snapshot.Shortcuts.Random,
                    ClickMode.Burst => snapshot.Shortcuts.Burst,
                    _ => snapshot.Shortcuts.Speed
                }
            };
            item.Click += (_, _) => _selectMode(mode);
            modes.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(modes);
        _contextMenu.Items.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem(AppStrings.T("Открыть настройки", "Open settings"));
        open.Click += (_, _) => { HideMenu(); _settings(); };
        _contextMenu.Items.Add(open);
        var exit = new ToolStripMenuItem(AppStrings.T("Выход", "Exit"));
        exit.Click += (_, _) => { HideMenu(); _exit(); };
        _contextMenu.Items.Add(exit);
    }

    internal static string ModeCaption(ClickMode mode) => mode switch
    {
        ClickMode.RandomInterval => AppStrings.T("Случайно", "Random"),
        ClickMode.Burst => AppStrings.T("Серии", "Burst"),
        _ => AppStrings.T("Скорость", "Speed")
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _contextMenu.Dispose();
        _flyout?.Dispose();
        _trayIcon?.Dispose();
    }
}
