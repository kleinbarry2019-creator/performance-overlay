using System.Drawing.Text;

namespace PerformanceOverlay;

internal sealed class OverlaySettingsForm : Form
{
    private readonly Action<OverlaySettings> _apply;
    private OverlaySettings _settings;
    private readonly ComboBox _fontFamily = new();
    private readonly NumericUpDown _fontSize = new();
    private readonly Button _textColor = new();
    private readonly Button _backgroundColor = new();
    private readonly TrackBar _transparency = new();
    private readonly Label _transparencyValue = new();
    private readonly NumericUpDown _cornerRadius = new();
    private readonly ComboBox _screen = new();
    private readonly NumericUpDown _offsetX = new();
    private readonly NumericUpDown _offsetY = new();
    private readonly NumericUpDown _refresh = new();
    private readonly NumericUpDown _temperatureRefresh = new();
    private readonly TextBox _pingTarget = new();
    private readonly CheckBox _clickThrough = new();
    private readonly CheckBox _safeMode = new();
    private readonly CheckBox _fpsTelemetry = new();
    private readonly CheckBox _suspendExcluded = new();
    private readonly ComboBox _hotkeyModifiers = new();
    private readonly ComboBox _hotkeyKey = new();

    public OverlaySettingsForm(OverlaySettings settings, Action<OverlaySettings> apply)
    {
        _settings = settings.Clone();
        _apply = apply;
        Text = "PerformanceOverlay – Einstellungen";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(900, 700);
        BackColor = Color.FromArgb(11, 16, 32);
        ForeColor = Color.FromArgb(229, 231, 235);
        Font = new Font("Segoe UI", 10F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(28, 24, 28, 22),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSettings(), 0, 1);
        root.Controls.Add(BuildFooter(), 0, 2);

        LoadValues();
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Overlay-Einstellungen",
            Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Darstellung, Position und Messverhalten direkt anpassen",
            Font = new Font("Segoe UI", 10.5F),
            ForeColor = Color.FromArgb(156, 163, 175),
            Location = new Point(3, 43)
        });
        return panel;
    }

    private Control BuildSettings()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor };
        var columns = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BackColor
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.Controls.Add(BuildAppearanceGroup(), 0, 0);
        columns.Controls.Add(BuildBehaviorGroup(), 1, 0);
        scroll.Controls.Add(columns);
        return scroll;
    }

    private Control BuildAppearanceGroup()
    {
        var group = CreateGroup("Darstellung");
        var grid = CreateGrid();
        AddRow(grid, "Schriftart", _fontFamily);
        AddRow(grid, "Schriftgröße", ConfigureNumeric(_fontSize, 7, 48, 0.5M, 11.5M, " pt"));
        AddColorRow(grid, "Textfarbe", _textColor, Color.White);
        AddColorRow(grid, "Hintergrund", _backgroundColor, Color.FromArgb(17, 24, 39));

        var transparencyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };
        _transparency.Minimum = 0;
        _transparency.Maximum = 255;
        _transparency.TickFrequency = 25;
        _transparency.Width = 180;
        _transparency.Height = 34;
        _transparency.Scroll += (_, _) => UpdateTransparencyLabel();
        _transparency.ValueChanged += (_, _) => UpdateTransparencyLabel();
        _transparencyValue.AutoSize = true;
        _transparencyValue.Padding = new Padding(4, 7, 0, 0);
        _transparencyValue.ForeColor = Color.FromArgb(191, 219, 254);
        transparencyPanel.Controls.Add(_transparency);
        transparencyPanel.Controls.Add(_transparencyValue);
        AddRow(grid, "Transparenz", transparencyPanel);

        AddRow(grid, "Eckenradius", ConfigureNumeric(_cornerRadius, 0, 40, 1, 9, " px"));
        AddRow(grid, "Bildschirm", _screen);
        AddRow(grid, "Position X", ConfigureNumeric(_offsetX, -5000, 5000, 1, 18, " px"));
        AddRow(grid, "Position Y", ConfigureNumeric(_offsetY, -5000, 5000, 1, 18, " px"));
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildBehaviorGroup()
    {
        var group = CreateGroup("Messung & Schutz");
        var grid = CreateGrid();
        AddRow(grid, "Aktualisierung", ConfigureNumeric(_refresh, 250, 5000, 50, 1000, " ms"));
        AddRow(grid, "Temperaturen", ConfigureNumeric(_temperatureRefresh, 1000, 30000, 500, 5000, " ms"));
        AddRow(grid, "Ping-Ziel", _pingTarget);
        var hotkeyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };
        ConfigureCombo(_hotkeyModifiers, HotkeyCatalog.ModifierOptions, 138);
        ConfigureCombo(_hotkeyKey, HotkeyCatalog.KeyOptions, 82);
        hotkeyPanel.Controls.Add(_hotkeyModifiers);
        hotkeyPanel.Controls.Add(_hotkeyKey);
        AddRow(grid, "Ein/Aus-Hotkey", hotkeyPanel);

        _clickThrough.Text = "Overlay klick-durchlässig (empfohlen)";
        _safeMode.Text = "Anti-Cheat-Safe-Modus";
        _fpsTelemetry.Text = "FPS-Messung über externes PresentMon";
        _suspendExcluded.Text = "Bei ausgeschlossenen Fenstern ausblenden";
        foreach (var check in new[] { _clickThrough, _safeMode, _fpsTelemetry, _suspendExcluded })
        {
            check.AutoSize = true;
            check.Padding = new Padding(0, 5, 0, 5);
            check.ForeColor = Color.FromArgb(203, 213, 225);
            check.FlatStyle = FlatStyle.Flat;
            grid.Controls.Add(check, 0, grid.RowCount);
            grid.SetColumnSpan(check, 2);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowCount++;
        }

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(350, 0),
            Text = "Safe Mode verwendet nur das aktive Fenstertitel-Muster. Es gibt keine Speicherzugriffe, DLL-Injektion oder Eingabe-Hooks.",
            ForeColor = Color.FromArgb(148, 163, 184),
            Padding = new Padding(0, 16, 0, 4)
        };
        grid.Controls.Add(note, 0, grid.RowCount);
        grid.SetColumnSpan(note, 2);
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowCount++;
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent
        };
        var apply = MakeButton("Übernehmen", Color.FromArgb(37, 99, 235));
        apply.Click += (_, _) => ApplyValues();
        var reset = MakeButton("Standardwerte", Color.FromArgb(55, 65, 81));
        reset.Click += (_, _) =>
        {
            _settings = new OverlaySettings();
            LoadValues();
        };
        var cancel = MakeButton("Abbrechen", Color.FromArgb(55, 65, 81));
        cancel.Click += (_, _) => Close();
        footer.Controls.Add(apply);
        footer.Controls.Add(cancel);
        footer.Controls.Add(reset);
        AcceptButton = apply;
        CancelButton = cancel;
        return footer;
    }

    private static Panel CreateGroup(string title)
    {
        var group = new Panel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 12, 16),
            Padding = new Padding(16, 12, 16, 16),
            BackColor = Color.FromArgb(20, 28, 48)
        };
        group.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1F);
            e.Graphics.DrawRectangle(pen, 0, 0, group.Width - 1, group.Height - 1);
        };
        group.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(16, 12)
        });
        return group;
    }

    private static TableLayoutPanel CreateGrid()
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 0,
            Margin = new Padding(0, 38, 0, 0),
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var caption = new Label
        {
            AutoSize = true,
            Text = label,
            ForeColor = Color.FromArgb(156, 163, 175),
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 0, 0)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void AddColorRow(TableLayoutPanel grid, string label, Button button, Color color)
    {
        button.Text = "Auswählen";
        button.Tag = color;
        button.BackColor = color;
        button.ForeColor = color.GetBrightness() > 0.55F ? Color.Black : Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 116, 139);
        button.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = (Color)button.Tag!, FullOpen = true };
            if (dialog.ShowDialog(button.FindForm()) == DialogResult.OK)
            {
                button.Tag = dialog.Color;
                button.BackColor = dialog.Color;
                button.ForeColor = dialog.Color.GetBrightness() > 0.55F ? Color.Black : Color.White;
            }
        };
        AddRow(grid, label, button);
    }

    private static NumericUpDown ConfigureNumeric(NumericUpDown control, decimal minimum, decimal maximum, decimal increment, decimal value, string suffix)
    {
        control.Minimum = minimum;
        control.Maximum = maximum;
        control.Increment = increment;
        control.Value = value;
        control.DecimalPlaces = increment < 1 ? 1 : 0;
        control.ThousandsSeparator = true;
        control.TextAlign = HorizontalAlignment.Left;
        control.BackColor = Color.FromArgb(30, 41, 59);
        control.ForeColor = Color.White;
        control.BorderStyle = BorderStyle.FixedSingle;
        control.Tag = suffix;
        return control;
    }

    private static Button MakeButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
    }

    private static void ConfigureCombo(ComboBox combo, IEnumerable<string> items, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Height = 30;
        combo.BackColor = Color.FromArgb(30, 41, 59);
        combo.ForeColor = Color.White;
        combo.Items.AddRange(items.Cast<object>().ToArray());
    }

    private void LoadValues()
    {
        _fontFamily.Items.Clear();
        using (var fonts = new InstalledFontCollection())
        {
            foreach (var family in fonts.Families.OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _fontFamily.Items.Add(family.Name);
            }
        }
        if (!_fontFamily.Items.Contains(_settings.FontFamily)) _fontFamily.Items.Add(_settings.FontFamily);
        _fontFamily.SelectedItem = _settings.FontFamily;
        _fontFamily.DropDownStyle = ComboBoxStyle.DropDownList;
        _fontFamily.BackColor = Color.FromArgb(30, 41, 59);
        _fontFamily.ForeColor = Color.White;

        _fontSize.Value = ClampDecimal((decimal)_settings.FontSize, _fontSize.Minimum, _fontSize.Maximum);
        _textColor.Tag = ParseColor(_settings.TextColor, Color.White);
        _backgroundColor.Tag = ParseColor(_settings.BackgroundColor, Color.FromArgb(17, 24, 39));
        foreach (var button in new[] { _textColor, _backgroundColor })
        {
            var color = (Color)button.Tag!;
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() > 0.55F ? Color.Black : Color.White;
        }
        _transparency.Value = Math.Clamp(255 - _settings.BackgroundOpacity, 0, 255);
        UpdateTransparencyLabel();
        _cornerRadius.Value = ClampDecimal(_settings.CornerRadius, _cornerRadius.Minimum, _cornerRadius.Maximum);
        _screen.Items.Clear();
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var screen = Screen.AllScreens[i];
            _screen.Items.Add($"{i}: {screen.DeviceName} ({screen.Bounds.Width}×{screen.Bounds.Height})");
        }
        _screen.SelectedIndex = Math.Min(_settings.ScreenIndex, Math.Max(0, _screen.Items.Count - 1));
        _offsetX.Value = ClampDecimal(_settings.OffsetX, _offsetX.Minimum, _offsetX.Maximum);
        _offsetY.Value = ClampDecimal(_settings.OffsetY, _offsetY.Minimum, _offsetY.Maximum);
        _refresh.Value = ClampDecimal(_settings.RefreshMilliseconds, _refresh.Minimum, _refresh.Maximum);
        _temperatureRefresh.Value = ClampDecimal(_settings.TemperatureRefreshMilliseconds, _temperatureRefresh.Minimum, _temperatureRefresh.Maximum);
        _pingTarget.Text = _settings.PingTarget;
        _hotkeyModifiers.SelectedItem = HotkeyCatalog.NormalizeModifiers(_settings.ToggleHotkeyModifiers);
        _hotkeyKey.SelectedItem = HotkeyCatalog.NormalizeKey(_settings.ToggleHotkeyKey);
        _clickThrough.Checked = _settings.ClickThrough;
        _safeMode.Checked = _settings.AntiCheatSafeMode;
        _fpsTelemetry.Checked = _settings.EnableFpsTelemetry;
        _suspendExcluded.Checked = _settings.SuspendOverlayForExcludedWindows;
    }

    private void ApplyValues()
    {
        _settings.FontFamily = _fontFamily.SelectedItem?.ToString() ?? "Segoe UI";
        _settings.FontSize = (float)_fontSize.Value;
        _settings.TextColor = ColorTranslator.ToHtml((Color)_textColor.Tag!);
        _settings.BackgroundColor = ColorTranslator.ToHtml((Color)_backgroundColor.Tag!);
        _settings.BackgroundOpacity = 255 - _transparency.Value;
        _settings.CornerRadius = (int)_cornerRadius.Value;
        _settings.ScreenIndex = Math.Max(0, _screen.SelectedIndex);
        _settings.OffsetX = (int)_offsetX.Value;
        _settings.OffsetY = (int)_offsetY.Value;
        _settings.RefreshMilliseconds = (int)_refresh.Value;
        _settings.TemperatureRefreshMilliseconds = (int)_temperatureRefresh.Value;
        _settings.PingTarget = string.IsNullOrWhiteSpace(_pingTarget.Text) ? "1.1.1.1" : _pingTarget.Text.Trim();
        _settings.ToggleHotkeyModifiers = HotkeyCatalog.NormalizeModifiers(_hotkeyModifiers.SelectedItem?.ToString());
        _settings.ToggleHotkeyKey = HotkeyCatalog.NormalizeKey(_hotkeyKey.SelectedItem?.ToString());
        _settings.ClickThrough = _clickThrough.Checked;
        _settings.AntiCheatSafeMode = _safeMode.Checked;
        _settings.EnableFpsTelemetry = _fpsTelemetry.Checked;
        _settings.SuspendOverlayForExcludedWindows = _suspendExcluded.Checked;
        _apply(_settings.Clone());
        _settings = _settings.Clone();
        FlashApplied();
    }

    private void FlashApplied()
    {
        Text = "PerformanceOverlay – gespeichert";
        BeginInvoke(new Action(() => Text = "PerformanceOverlay – Einstellungen"));
    }

    private void UpdateTransparencyLabel()
    {
        _transparencyValue.Text = $"{_transparency.Value / 255.0:P0} transparent";
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum) => Math.Clamp(value, minimum, maximum);

    private static Color ParseColor(string value, Color fallback)
    {
        try { return ColorTranslator.FromHtml(value); } catch { return fallback; }
    }
}
