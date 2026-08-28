using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace PerformanceOverlay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool requestSettings = args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase));
        bool requestToggle = args.Any(arg => string.Equals(arg, "--toggle", StringComparison.OrdinalIgnoreCase));
        bool requestExit = args.Any(arg => string.Equals(arg, "--exit", StringComparison.OrdinalIgnoreCase));
        using var mutex = new Mutex(true, "Global\\PerformanceOverlay.Singleton", out bool createdNew);
        if (!createdNew)
        {
            if (requestSettings) OverlayCommandBus.Send("settings");
            else if (requestToggle) OverlayCommandBus.Send("toggle");
            else if (requestExit) OverlayCommandBus.Send("exit");
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayApplicationContext(requestSettings));
    }
}

internal static class OverlayCommandBus
{
    private static string CommandPath => Path.Combine(SettingsStore.DirectoryPath, ".command");

    public static void Send(string command)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            string temporaryPath = CommandPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, command);
            File.Move(temporaryPath, CommandPath, true);
        }
        catch
        {
            // A concurrently handled command can be retried from the shortcut.
        }
    }

    public static string? Consume()
    {
        try
        {
            if (!File.Exists(CommandPath)) return null;
            string command = File.ReadAllText(CommandPath).Trim();
            File.Delete(CommandPath);
            return command;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class OverlaySettings
{
    public int RefreshMilliseconds { get; set; } = 1000;
    public int TemperatureRefreshMilliseconds { get; set; } = 5000;
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 11.5f;
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#111827";
    public int BackgroundOpacity { get; set; } = 205;
    public int CornerRadius { get; set; } = 9;
    public int ScreenIndex { get; set; } = 0;
    public int OffsetX { get; set; } = 18;
    public int OffsetY { get; set; } = 18;
    public bool ClickThrough { get; set; } = true;
    public bool AntiCheatSafeMode { get; set; } = false;
    public bool EnableFpsTelemetry { get; set; } = true;
    public bool SuspendOverlayForExcludedWindows { get; set; } = false;
    public string ToggleHotkeyModifiers { get; set; } = "Control+Shift";
    public string ToggleHotkeyKey { get; set; } = "F10";
    public string[] FpsExcludedWindowTitleFragments { get; set; } = ["Call of Duty"];
    public string PingTarget { get; set; } = "1.1.1.1";
    public string? PresentMonPath { get; set; }

    public OverlaySettings Clone() => new()
    {
        RefreshMilliseconds = RefreshMilliseconds,
        TemperatureRefreshMilliseconds = TemperatureRefreshMilliseconds,
        FontFamily = FontFamily,
        FontSize = FontSize,
        TextColor = TextColor,
        BackgroundColor = BackgroundColor,
        BackgroundOpacity = BackgroundOpacity,
        CornerRadius = CornerRadius,
        ScreenIndex = ScreenIndex,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        ClickThrough = ClickThrough,
        AntiCheatSafeMode = AntiCheatSafeMode,
        EnableFpsTelemetry = EnableFpsTelemetry,
        SuspendOverlayForExcludedWindows = SuspendOverlayForExcludedWindows,
        ToggleHotkeyModifiers = ToggleHotkeyModifiers,
        ToggleHotkeyKey = ToggleHotkeyKey,
        FpsExcludedWindowTitleFragments = FpsExcludedWindowTitleFragments.ToArray(),
        PingTarget = PingTarget,
        PresentMonPath = PresentMonPath
    };
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PerformanceOverlay");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static OverlaySettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings is not null)
                {
                    return Normalize(settings);
                }
            }
        }
        catch
        {
            // A malformed settings file must not prevent the overlay from starting.
        }

        var defaults = Normalize(new OverlaySettings());
        Save(defaults);
        return defaults;
    }

    public static void Save(OverlaySettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    private static OverlaySettings Normalize(OverlaySettings settings)
    {
        settings.RefreshMilliseconds = Math.Clamp(settings.RefreshMilliseconds, 250, 5000);
        settings.TemperatureRefreshMilliseconds = Math.Clamp(settings.TemperatureRefreshMilliseconds, 1000, 30000);
        settings.FontSize = Math.Clamp(settings.FontSize, 7, 48);
        settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 255);
        settings.CornerRadius = Math.Clamp(settings.CornerRadius, 0, 40);
        settings.ScreenIndex = Math.Max(0, settings.ScreenIndex);
        settings.OffsetX = Math.Clamp(settings.OffsetX, -5000, 5000);
        settings.OffsetY = Math.Clamp(settings.OffsetY, -5000, 5000);
        settings.PingTarget = string.IsNullOrWhiteSpace(settings.PingTarget) ? "1.1.1.1" : settings.PingTarget.Trim();
        settings.FontFamily = string.IsNullOrWhiteSpace(settings.FontFamily) ? "Segoe UI" : settings.FontFamily.Trim();
        settings.TextColor = IsHexColor(settings.TextColor) ? settings.TextColor : "#FFFFFF";
        settings.BackgroundColor = IsHexColor(settings.BackgroundColor) ? settings.BackgroundColor : "#111827";
        settings.ToggleHotkeyModifiers = HotkeyCatalog.NormalizeModifiers(settings.ToggleHotkeyModifiers);
        settings.ToggleHotkeyKey = HotkeyCatalog.NormalizeKey(settings.ToggleHotkeyKey);
        settings.FpsExcludedWindowTitleFragments = (settings.FpsExcludedWindowTitleFragments ?? Array.Empty<string>())
            .Select(fragment => fragment.Trim())
            .Where(fragment => fragment.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();
        return settings;
    }

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            _ = ColorTranslator.FromHtml(value);
            return value.StartsWith('#') && (value.Length == 7 || value.Length == 9);
        }
        catch
        {
            return false;
        }
    }
}

internal static class HotkeyCatalog
{
    public static readonly string[] ModifierOptions = [
        "Control+Shift", "Control+Alt", "Alt+Shift", "Control+Windows", "Alt+Windows", "Shift+Windows"
    ];

    public static readonly string[] KeyOptions = [
        .. Enumerable.Range(1, 12).Select(index => $"F{index}"),
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString()),
        .. Enumerable.Range(0, 10).Select(value => value.ToString())
    ];

    public static string NormalizeModifiers(string? value) =>
        ModifierOptions.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) ?? ModifierOptions[0];

    public static string NormalizeKey(string? value) =>
        KeyOptions.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase)) ?? "F10";

    public static bool TryParse(string? modifierText, string? keyText, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (string part in NormalizeModifiers(modifierText).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            modifiers |= part switch
            {
                "Control" => 0x0002u,
                "Alt" => 0x0001u,
                "Shift" => 0x0004u,
                "Windows" => 0x0008u,
                _ => 0u
            };
        }

        string normalizedKey = NormalizeKey(keyText);
        if (normalizedKey.StartsWith('F') && int.TryParse(normalizedKey[1..], out int functionNumber))
        {
            key = (uint)(Keys.F1 + functionNumber - 1);
            return true;
        }
        if (normalizedKey.Length == 1 && char.IsLetter(normalizedKey[0]))
        {
            key = (uint)(Keys.A + char.ToUpperInvariant(normalizedKey[0]) - 'A');
            return true;
        }
        if (normalizedKey.Length == 1 && char.IsDigit(normalizedKey[0]))
        {
            key = (uint)(Keys.D0 + normalizedKey[0] - '0');
            return true;
        }
        return false;
    }
}

internal sealed record MetricsSnapshot(
    double? Fps,
    double CpuUsage,
    double GpuUsage,
    double? GpuTemperature,
    double DownloadKibPerSecond,
    double UploadKibPerSecond,
    double? PacketLossPercent,
    double? PingMilliseconds,
    string TargetProcess,
    string FpsSource,
    DateTimeOffset Timestamp,
    bool CompatibilitySuspended)
{
    public static MetricsSnapshot Empty => new(null, 0, 0, null, 0, 0, null, null, "Desktop", "PresentMon fehlt", DateTimeOffset.Now, false);
}

internal sealed class OverlayApplicationContext : ApplicationContext
{
    private OverlaySettings _settings;
    private readonly OverlayForm _form;
    private readonly MetricsSampler _sampler;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private OverlaySettingsForm? _settingsForm;
    private bool _sampling;

    public OverlayApplicationContext(bool openSettings)
    {
        _settings = SettingsStore.Load();
        _sampler = new MetricsSampler();
        _form = new OverlayForm(_settings);
        _form.FormClosed += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Overlay anzeigen/verbergen", null, (_, _) => _form.ToggleVisible());
        menu.Items.Add("Klick-Durchlässigkeit umschalten", null, (_, _) => ToggleClickThrough());
        menu.Items.Add("Anti-Cheat-Safe-Modus umschalten", null, (_, _) => ToggleAntiCheatSafeMode());
        menu.Items.Add("Overlay-Einstellungen …", null, (_, _) => OpenSettingsUi());
        menu.Items.Add("Konfigurationsdatei öffnen", null, (_, _) => OpenRawSettings());
        menu.Items.Add("Messquelle zurücksetzen", null, (_, _) => _sampler.ResetFpsProvider());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "Performance Overlay",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => _form.ToggleVisible();

        _timer = new System.Windows.Forms.Timer { Interval = _settings.RefreshMilliseconds };
        _timer.Tick += (_, _) =>
        {
            HandleExternalCommand();
            _ = SampleAndRenderAsync();
        };
        _timer.Start();
        _form.Show();
        if (openSettings) OpenSettingsUi();
    }

    private void HandleExternalCommand()
    {
        switch (OverlayCommandBus.Consume())
        {
            case "settings": OpenSettingsUi(); break;
            case "toggle": _form.ToggleVisible(); break;
            case "exit": ExitThread(); break;
        }
    }

    private async Task SampleAndRenderAsync()
    {
        if (_sampling || _form.IsDisposed) return;
        _sampling = true;
        try
        {
            var snapshot = await _sampler.SampleAsync(_settings, CancellationToken.None);
            if (!_form.IsDisposed)
            {
                _form.UpdateSnapshot(snapshot);
                _form.SetCompatibilitySuspended(snapshot.CompatibilitySuspended);
            }
        }
        finally
        {
            _sampling = false;
        }
    }

    private void ToggleClickThrough()
    {
        _settings.ClickThrough = !_settings.ClickThrough;
        SettingsStore.Save(_settings);
        _form.ApplySettings(_settings);
    }

    private void ToggleAntiCheatSafeMode()
    {
        _settings.AntiCheatSafeMode = !_settings.AntiCheatSafeMode;
        SettingsStore.Save(_settings);
        _form.ApplySettings(_settings);
        _form.ShowBalloon("Performance Overlay", _settings.AntiCheatSafeMode
            ? "Anti-Cheat-Safe-Modus aktiviert."
            : "Anti-Cheat-Safe-Modus deaktiviert; nur für ausdrücklich unterstützte Spiele verwenden.");
    }

    private void OpenSettingsUi()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new OverlaySettingsForm(_settings, updatedSettings =>
            {
                _settings = updatedSettings;
                SettingsStore.Save(_settings);
                _timer.Interval = _settings.RefreshMilliseconds;
                _form.ApplySettings(_settings);
            });
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }

        if (!_settingsForm.Visible) _settingsForm.Show();
        _settingsForm.WindowState = FormWindowState.Normal;
        _settingsForm.Activate();
    }

    private void OpenRawSettings()
    {
        SettingsStore.Save(_settings);
        Process.Start(new ProcessStartInfo("notepad.exe", SettingsStore.FilePath) { UseShellExecute = true });
        _form.ShowBalloon("Performance Overlay", "Einstellungen gespeichert. Nach dem Bearbeiten im Tray-Menü neu starten.");
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _sampler.Dispose();
        _form.Close();
        base.ExitThreadCore();
    }
}

internal sealed class OverlayForm : Form
{
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int WmHotkey = 0x0312;
    private const int ToggleHotkeyId = 0x4F56;
    private OverlaySettings _settings;
    private MetricsSnapshot _snapshot = MetricsSnapshot.Empty;
    private readonly List<string> _metricTexts = new();
    private readonly List<int> _metricWidths = new();
    private static readonly string[] MetricWidthSamples =
    {
        "9999 FPS",
        "100% CPU",
        "100% - 100°C GPU",
        "↓9999999 ↑9999999 KiB/s NET",
        "100.0% LOSS"
    };
    private bool _userVisible = true;
    private bool _compatibilitySuspended;
    private bool _hotkeyRegistered;

    public OverlayForm(OverlaySettings settings)
    {
        _settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        Padding = new Padding(7, 5, 7, 5);
        ApplySettings(settings);
        UpdateSnapshot(MetricsSnapshot.Empty);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExLayered;
            // WinForms can request CreateParams from the base Form constructor
            // before the derived constructor has assigned its settings field.
            if (_settings?.ClickThrough == true) cp.ExStyle |= WsExTransparent;
            return cp;
        }
    }

    public void ApplySettings(OverlaySettings settings)
    {
        _settings = settings;
        try
        {
            BackColor = ColorTranslator.FromHtml(settings.BackgroundColor);
            ForeColor = ColorTranslator.FromHtml(settings.TextColor);
        }
        catch
        {
            BackColor = Color.FromArgb(17, 24, 39);
            ForeColor = Color.White;
        }

        // Do not use Form.Opacity here: it would make the metric text translucent too.
        // RenderLayeredSurface applies alpha only to the background brush.
        Opacity = 1.0;
        try
        {
            Font = new Font(settings.FontFamily, settings.FontSize, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch
        {
            Font = new Font("Segoe UI", settings.FontSize, FontStyle.Regular, GraphicsUnit.Point);
        }
        RecreateHandle();
        PlaceOnSelectedScreen();
        UpdateSnapshot(_snapshot);
        ApplyVisibility();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterToggleHotkey();
        RenderLayeredSurface();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterToggleHotkey();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == ToggleHotkeyId)
        {
            ToggleVisible();
        }
        base.WndProc(ref m);
    }

    private void RegisterToggleHotkey()
    {
        if (_hotkeyRegistered || !HotkeyCatalog.TryParse(_settings.ToggleHotkeyModifiers, _settings.ToggleHotkeyKey, out uint modifiers, out uint key)) return;
        _hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, ToggleHotkeyId, modifiers | 0x4000u, key);
    }

    private void UnregisterToggleHotkey()
    {
        if (!_hotkeyRegistered || !IsHandleCreated) return;
        NativeMethods.UnregisterHotKey(Handle, ToggleHotkeyId);
        _hotkeyRegistered = false;
    }

    public void UpdateSnapshot(MetricsSnapshot snapshot)
    {
        _snapshot = snapshot;
        _metricTexts.Clear();
        AddMetric(snapshot.Fps is null ? "-- FPS" : $"{snapshot.Fps:0} FPS");
        AddMetric($"{snapshot.CpuUsage:0}% CPU");
        AddMetric($"{snapshot.GpuUsage:0}% - {FormatTemperature(snapshot.GpuTemperature)} GPU");
        AddMetric($"↓{snapshot.DownloadKibPerSecond:0} ↑{snapshot.UploadKibPerSecond:0} KiB/s NET");
        AddMetric(snapshot.PacketLossPercent is null ? "-- LOSS" : $"{snapshot.PacketLossPercent:0.0}% LOSS");
        _metricWidths.Clear();
        int width = Padding.Horizontal;
        for (int index = 0; index < _metricTexts.Count; index++)
        {
            Size reserved = TextRenderer.MeasureText(MetricWidthSamples[index], Font, Size.Empty, TextFormatFlags.NoPadding);
            _metricWidths.Add(reserved.Width);
            width += reserved.Width + 12;
            if (index < _metricTexts.Count - 1)
            {
                Size separator = TextRenderer.MeasureText("|", Font, Size.Empty, TextFormatFlags.NoPadding);
                width += separator.Width + 12;
            }
        }
        Width = Math.Max(40, width);
        Height = Math.Max(26, (int)Math.Ceiling(Font.GetHeight()) + Padding.Vertical);
        RenderLayeredSurface();
    }

    private static string FormatTemperature(double? value) => value is null ? "--" : $"{value:0}°C";

    private void AddMetric(string text) => _metricTexts.Add(text);

    private void RenderLayeredSurface()
    {
        if (!IsHandleCreated || Width < 2 || Height < 2) return;

        IntPtr screenDc = IntPtr.Zero;
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;
        IntPtr previousBitmap = IntPtr.Zero;
        try
        {
            using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(Color.FromArgb(_settings.BackgroundOpacity, ParseColor(_settings.BackgroundColor, Color.FromArgb(17, 24, 39)))))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var stringFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Color.Transparent);
                using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), _settings.CornerRadius);
                graphics.FillPath(background, path);

                float x = Padding.Left;
                float y = Math.Max(0, (Height - Font.GetHeight()) / 2F - 1F);
                for (int index = 0; index < _metricTexts.Count; index++)
                {
                    string text = _metricTexts[index];
                    var metricBounds = new RectangleF(x, 0, _metricWidths[index], Height);
                    graphics.DrawString(text, Font, textBrush, metricBounds, stringFormat);
                    x += _metricWidths[index] + 12F;
                    if (index < _metricTexts.Count - 1)
                    {
                        graphics.DrawString("|", Font, textBrush, x, y, stringFormat);
                        x += graphics.MeasureString("|", Font, PointF.Empty, stringFormat).Width + 12F;
                    }
                }
            }

            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            previousBitmap = NativeMethods.SelectObject(memoryDc, bitmapHandle);
            var destination = new NativeMethods.Point(Location.X, Location.Y);
            var size = new NativeMethods.Size(Width, Height);
            var source = new NativeMethods.Point(0, 0);
            var blend = new NativeMethods.BlendFunction(0, 0, 255, 1);
            NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2);
        }
        catch
        {
            // A display mode change can invalidate a layered surface mid-frame.
            // The next metric tick will render it again.
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previousBitmap);
            if (bitmapHandle != IntPtr.Zero) NativeMethods.DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void ToggleVisible()
    {
        _userVisible = !_userVisible;
        ApplyVisibility();
    }

    public void SetCompatibilitySuspended(bool suspended)
    {
        _compatibilitySuspended = suspended;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        bool shouldBeVisible = _userVisible && !_compatibilitySuspended;
        if (shouldBeVisible)
        {
            PlaceOnSelectedScreen();
            if (!Visible) Show();
            // Win+D/"Desktop anzeigen" can hide top-level tool windows at the
            // Win32 level without changing the WinForms Visible property.
            // Restore only when the user has the overlay enabled.
            if (IsHandleCreated && !NativeMethods.IsWindowVisible(Handle))
                NativeMethods.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
        }
        else if (Visible)
        {
            Hide();
        }
    }

    public void ShowBalloon(string title, string message)
    {
        using var icon = new NotifyIcon { Icon = SystemIcons.Information, Visible = true };
        icon.ShowBalloonTip(2500, title, message, ToolTipIcon.Info);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RenderLayeredSurface();
    }

    private void PlaceOnSelectedScreen()
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0) return;
        var screen = screens[Math.Min(_settings.ScreenIndex, screens.Length - 1)];
        Location = new Point(screen.WorkingArea.Left + _settings.OffsetX, screen.WorkingArea.Top + _settings.OffsetY);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try { return ColorTranslator.FromHtml(value); } catch { return fallback; }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr SelectObject(IntPtr hDc, IntPtr objectHandle);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr objectHandle);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hDc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hDcDst, ref Point pointDst, ref Size size,
            IntPtr hDcSrc, ref Point pointSrc, int colorKey, ref BlendFunction blend, int flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point(int x, int y)
        {
            public int X = x;
            public int Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Size(int width, int height)
        {
            public int Width = width;
            public int Height = height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BlendFunction(byte operation, byte flags, byte sourceConstantAlpha, byte alphaFormat)
        {
            public byte Operation = operation;
            public byte Flags = flags;
            public byte SourceConstantAlpha = sourceConstantAlpha;
            public byte AlphaFormat = alphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int command);
    }
}

internal sealed class MetricChip : Control
{
    public MetricChip(string text, Color color, Font font)
    {
        Text = text;
        ForeColor = color;
        Font = font;
        AutoSize = true;
        Margin = new Padding(6, 0, 6, 0);
        Padding = new Padding(0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        var measured = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        Size = new Size(measured.Width, Math.Max(measured.Height, 16));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, Color.Transparent,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

internal sealed class MetricsSampler : IDisposable
{
    private readonly CpuUsageReader _cpu = new();
    private readonly NetworkRateReader _network = new();
    private readonly PacketLossReader _packetLoss = new();
    private readonly GpuReader _gpu = new();
    private readonly DesktopFpsReader _desktopFps = new();
    private readonly PresentMonFpsProvider _fps = new();
    private DateTimeOffset _lastTemperatureRead = DateTimeOffset.MinValue;
    private double? _gpuTemperature;
    private double _gpuUsage;

    public async Task<MetricsSnapshot> SampleAsync(OverlaySettings settings, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        double cpuUsage = _cpu.ReadPercent();
        var network = _network.ReadRates();
        var target = ForegroundProcess.Get();
        bool excludedBySafeMode = settings.AntiCheatSafeMode
            && settings.SuspendOverlayForExcludedWindows
            && target.Matches(settings.FpsExcludedWindowTitleFragments);
        double? fps;
        string fpsSource;
        if (settings.EnableFpsTelemetry && target.ProcessId == 0)
        {
            if (!string.Equals(_fps.Source, "DWM Desktop", StringComparison.Ordinal)) _fps.Pause("DWM Desktop");
            fps = _desktopFps.Read();
            fpsSource = "DWM Desktop";
        }
        else if (settings.EnableFpsTelemetry && !excludedBySafeMode)
        {
            await _fps.EnsureProcessAsync(target.ProcessId, settings.PresentMonPath, cancellationToken);
            fps = _fps.ReadFps();
            fpsSource = _fps.Source;
            // Normal windows do not expose game-style frame presents. Keep a
            // useful live value on every surface by using the DWM display
            // cadence as a fallback. Known protected/game titles remain blank
            // until PresentMon supplies real frames.
            if (fps is null && !target.Matches(settings.FpsExcludedWindowTitleFragments))
            {
                fps = _desktopFps.Read();
                fpsSource = "DWM Oberfläche";
            }
        }
        else
        {
            string reason = excludedBySafeMode ? "FPS pausiert (Safe-Modus)" : "FPS deaktiviert";
            if (!string.Equals(_fps.Source, reason, StringComparison.Ordinal)) _fps.Pause(reason);
            fps = null;
            fpsSource = reason;
        }

        if (now - _lastTemperatureRead >= TimeSpan.FromMilliseconds(settings.TemperatureRefreshMilliseconds))
        {
            _lastTemperatureRead = now;
            var gpu = await _gpu.ReadAsync(cancellationToken);
            _gpuUsage = gpu.Usage;
            _gpuTemperature = gpu.Temperature;
        }

        var ping = await _packetLoss.ReadAsync(settings.PingTarget, cancellationToken);
        return new MetricsSnapshot(fps, cpuUsage, _gpuUsage, _gpuTemperature,
            network.DownloadKibPerSecond, network.UploadKibPerSecond, ping.LossPercent,
            ping.LatencyMilliseconds, target.Name, fpsSource, now,
            excludedBySafeMode && settings.SuspendOverlayForExcludedWindows);
    }

    public void ResetFpsProvider() => _fps.Reset();

    public void Dispose()
    {
        _fps.Dispose();
    }
}

internal sealed class CpuUsageReader
{
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _initialized;

    public double ReadPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong idleValue = ToUInt64(idle);
        ulong kernelValue = ToUInt64(kernel);
        ulong userValue = ToUInt64(user);
        if (!_initialized)
        {
            _lastIdle = idleValue;
            _lastKernel = kernelValue;
            _lastUser = userValue;
            _initialized = true;
            return 0;
        }

        ulong idleDelta = idleValue - _lastIdle;
        ulong systemDelta = (kernelValue - _lastKernel) + (userValue - _lastUser);
        _lastIdle = idleValue;
        _lastKernel = kernelValue;
        _lastUser = userValue;
        return systemDelta == 0 ? 0 : Math.Clamp((1d - idleDelta / (double)systemDelta) * 100d, 0, 100);
    }

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME value) =>
        ((ulong)(uint)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);
}

internal sealed class NetworkRateReader
{
    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;

    public (double DownloadKibPerSecond, double UploadKibPerSecond) ReadRates()
    {
        long received = 0;
        long sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch
            {
                // An adapter can disappear while the list is being sampled.
            }
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (_lastTimestamp == 0)
        {
            _lastReceived = received;
            _lastSent = sent;
            _lastTimestamp = timestamp;
            return (0, 0);
        }

        double seconds = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
        double down = seconds <= 0 ? 0 : Math.Max(0, received - _lastReceived) / 1024d / seconds;
        double up = seconds <= 0 ? 0 : Math.Max(0, sent - _lastSent) / 1024d / seconds;
        _lastReceived = received;
        _lastSent = sent;
        _lastTimestamp = timestamp;
        return (down, up);
    }
}

internal sealed class PacketLossReader
{
    private readonly Queue<bool> _samples = new();
    private DateTimeOffset _lastPing = DateTimeOffset.MinValue;
    private double? _lastLatency;

    public async Task<(double? LossPercent, double? LatencyMilliseconds)> ReadAsync(string host, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _lastPing < TimeSpan.FromSeconds(3))
        {
            return (CalculateLoss(), _lastLatency);
        }

        _lastPing = DateTimeOffset.UtcNow;
        bool success = false;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1000).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            success = reply.Status == IPStatus.Success;
            _lastLatency = success ? reply.RoundtripTime : null;
        }
        catch
        {
            _lastLatency = null;
        }

        _samples.Enqueue(success);
        while (_samples.Count > 20) _samples.Dequeue();
        return (CalculateLoss(), _lastLatency);
    }

    private double? CalculateLoss() => _samples.Count == 0 ? null : 100d * _samples.Count(sample => !sample) / _samples.Count;
}

internal sealed record GpuSnapshot(double Usage, double? Temperature);

internal sealed class GpuReader
{
    public async Task<GpuSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = FindOnPath("nvidia-smi.exe") ?? FindOnPath("nvidia-smi");
        if (executable is null) return new GpuSnapshot(0, null);

        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--query-gpu=utilization.gpu,temperature.gpu");
            psi.ArgumentList.Add("--format=csv,noheader,nounits");
            using var process = Process.Start(psi);
            if (process is null) return new GpuSnapshot(0, null);
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var parts = output.Trim().Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return new GpuSnapshot(0, null);
            double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double usage);
            double? temperature = double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double temp) ? temp : null;
            return new GpuSnapshot(Math.Clamp(usage, 0, 100), temperature);
        }
        catch
        {
            return new GpuSnapshot(0, null);
        }
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }
}

internal sealed record ForegroundProcessInfo(int ProcessId, string Name, string WindowTitle)
{
    public bool Matches(string[] fragments) => fragments.Any(fragment =>
        WindowTitle.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}

internal static class ForegroundProcess
{
    public static ForegroundProcessInfo Get()
    {
        IntPtr handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return new ForegroundProcessInfo(0, "Desktop", string.Empty);
        GetWindowThreadProcessId(handle, out uint processId);
        string title = GetWindowTitle(handle);
        string className = GetWindowClassName(handle);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "DV2ControlHost"
            or "XamlExplorerHostIslandWindow_WASDK" or "Windows.UI.Core.CoreWindow")
            return new ForegroundProcessInfo(0, "Desktop", title);
        return new ForegroundProcessInfo((int)processId, "Active window", title);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        int length = GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;
        var buffer = new System.Text.StringBuilder(length + 1);
        _ = GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var buffer = new System.Text.StringBuilder(256);
        return GetClassName(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);
}

internal sealed class DesktopFpsReader
{
    private ulong _lastFrame;
    private ulong _lastRefresh;
    private long _lastTimestamp;

    public double? Read()
    {
        var timing = new DwmTimingInfo { Size = (uint)Marshal.SizeOf<DwmTimingInfo>() };
        if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) != 0)
            return ReadDisplayRefreshRate();

        long timestamp = Stopwatch.GetTimestamp();
        double? observed = null;
        if (_lastTimestamp != 0)
        {
            double seconds = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0 && timing.Frame >= _lastFrame)
            {
                ulong frameDelta = timing.Frame - _lastFrame;
                double rate = frameDelta / seconds;
                if (rate is >= 1 and <= 1000) observed = rate;
            }

            if (observed is null && seconds > 0 && timing.RefreshCount >= _lastRefresh)
            {
                ulong refreshDelta = timing.RefreshCount - _lastRefresh;
                double rate = refreshDelta / seconds;
                if (rate is >= 1 and <= 1000) observed = rate;
            }
        }

        _lastFrame = timing.Frame;
        _lastRefresh = timing.RefreshCount;
        _lastTimestamp = timestamp;
        double compositionRate = timing.ComposeRate.Denominator == 0
            ? 0
            : timing.ComposeRate.Numerator / (double)timing.ComposeRate.Denominator;
        double refreshRate = timing.RefreshRate.Denominator == 0
            ? 0
            : timing.RefreshRate.Numerator / (double)timing.RefreshRate.Denominator;
        return observed
            ?? (compositionRate is >= 1 and <= 1000 ? compositionRate
                : refreshRate is >= 1 and <= 1000 ? refreshRate : ReadDisplayRefreshRate());
    }

    private static double? ReadDisplayRefreshRate()
    {
        IntPtr deviceContext = GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return null;
        try
        {
            int refreshRate = GetDeviceCaps(deviceContext, 116); // VREFRESH
            return refreshRate is >= 1 and <= 1000 ? refreshRate : null;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr window, ref DwmTimingInfo timing);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnsignedRatio
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmTimingInfo
    {
        public uint Size;
        public UnsignedRatio RefreshRate;
        public long RefreshPeriod;
        public UnsignedRatio ComposeRate;
        public long VBlank;
        public ulong RefreshCount;
        public uint DxRefresh;
        public long Compose;
        public ulong Frame;
        public uint DxPresent;
        public ulong RefreshFrame;
        public ulong FrameSubmitted;
        public uint DxPresentSubmitted;
        public ulong FrameConfirmed;
        public uint DxPresentConfirmed;
        public ulong RefreshConfirmed;
        public uint DxRefreshConfirmed;
        public ulong FramesLate;
        public uint FramesOutstanding;
        public ulong FrameDisplayed;
        public long QpcFrameDisplayed;
        public ulong RefreshFrameDisplayed;
        public ulong FrameComplete;
        public long QpcFrameComplete;
        public ulong FramePending;
        public long QpcFramePending;
        public ulong FramesDisplayed;
        public ulong FramesComplete;
        public ulong FramesPending;
        public ulong FramesAvailable;
        public ulong FramesDropped;
        public ulong FramesMissed;
        public ulong RefreshNextDisplayed;
        public ulong RefreshNextPresented;
        public ulong RefreshesDisplayed;
        public ulong RefreshesPresented;
        public ulong RefreshStarted;
        public ulong PixelsReceived;
        public ulong PixelsDrawn;
        public ulong BuffersEmpty;
    }
}

internal sealed class PresentMonFpsProvider : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<long> _frames = new();
    private Process? _process;
    private int _targetProcessId;
    private int _processIdColumn = -1;
    private CancellationTokenSource _readerCancellation = new();
    public string Source { get; private set; } = "PresentMon fehlt";

    public Task EnsureProcessAsync(int processId, string? configuredPath, CancellationToken cancellationToken)
    {
        if (processId <= 0) return Task.CompletedTask;
        if (_process is { HasExited: false } && _targetProcessId == processId) return Task.CompletedTask;

        StopProcess();
        string? executable = ResolveExecutable(configuredPath);
        if (executable is null)
        {
            Source = "PresentMon fehlt";
            return Task.CompletedTask;
        }

        try
        {
            _readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--process_id");
            psi.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--output_stdout");
            psi.ArgumentList.Add("--no_console_stats");
            psi.ArgumentList.Add("--exclude_dropped");
            psi.ArgumentList.Add("--terminate_on_proc_exit");
            psi.ArgumentList.Add("--session_name");
            psi.ArgumentList.Add($"PerformanceOverlay-{Environment.ProcessId}");
            _process = Process.Start(psi);
            if (_process is null)
            {
                Source = "PresentMon konnte nicht starten";
                return Task.CompletedTask;
            }

            _targetProcessId = processId;
            _processIdColumn = -1;
            lock (_gate) _frames.Clear();
            Source = "PresentMon";
            _ = ReadOutputAsync(_process, _readerCancellation.Token);
        }
        catch
        {
            Source = "PresentMon nicht verfügbar";
            StopProcess();
        }
        return Task.CompletedTask;
    }

    public double? ReadFps()
    {
        lock (_gate)
        {
            long now = Stopwatch.GetTimestamp();
            long cutoff = now - (long)(Stopwatch.Frequency * 1.5);
            while (_frames.Count > 0 && _frames.Peek() < cutoff) _frames.Dequeue();
            if (_frames.Count < 2) return null;
            return _frames.Count / 1.5;
        }
    }

    public void Reset() => Pause("PresentMon zurückgesetzt");

    public void Pause(string reason)
    {
        StopProcess();
        Source = reason;
    }

    private async Task ReadOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;
                ConsumeCsvLine(line);
            }
        }
        catch
        {
            // PresentMon can exit when a game closes or when ETW permissions are unavailable.
        }
    }

    private void ConsumeCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var fields = ParseCsv(line);
        if (_processIdColumn < 0)
        {
            _processIdColumn = fields.FindIndex(field => string.Equals(field.Trim(), "ProcessID", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (_processIdColumn >= fields.Count || !int.TryParse(fields[_processIdColumn], out int processId) || processId != _targetProcessId) return;
        lock (_gate)
        {
            _frames.Enqueue(Stopwatch.GetTimestamp());
            while (_frames.Count > 4000) _frames.Dequeue();
        }
    }

    private static List<string> ParseCsv(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char character in line.TrimStart('\uFEFF'))
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (character == ',' && !quoted) { values.Add(current.ToString()); current.Clear(); continue; }
            current.Append(character);
        }
        values.Add(current.ToString());
        return values;
    }

    private static string? ResolveExecutable(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe"));
        candidates.Add("PresentMon.exe");
        foreach (string candidate in candidates)
        {
            if (candidate.Contains(Path.DirectorySeparatorChar) || candidate.Contains(Path.AltDirectorySeparatorChar))
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            else if (FindOnPath(candidate) is string path)
            {
                return path;
            }
        }
        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private void StopProcess()
    {
        try { _readerCancellation.Cancel(); } catch { }
        _readerCancellation.Dispose();
        _readerCancellation = new CancellationTokenSource();
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
        _targetProcessId = 0;
        lock (_gate) _frames.Clear();
    }

    public void Dispose() => StopProcess();
}

internal static class SelfTest
{
    public static void Run()
    {
        var settings = new OverlaySettings { RefreshMilliseconds = 1, BackgroundOpacity = 999, FontSize = 99 };
        string path = Path.Combine(Path.GetTempPath(), $"overlay-self-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
            var roundTrip = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path));
            if (roundTrip is null || roundTrip.RefreshMilliseconds != 1) throw new InvalidOperationException("Settings round-trip failed.");
            if (new DesktopFpsReader().Read() is not > 0) throw new InvalidOperationException("DWM desktop FPS unavailable.");
            Console.WriteLine("PerformanceOverlay self-test: PASS");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

