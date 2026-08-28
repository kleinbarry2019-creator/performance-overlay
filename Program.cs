using System.Diagnostics;
using System.Drawing.Drawing2D;
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
        using var mutex = new Mutex(true, "Global\\PerformanceOverlay.Singleton", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            SelfTest.Run();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new OverlayApplicationContext());
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
    public bool AntiCheatSafeMode { get; set; } = true;
    public bool EnableFpsTelemetry { get; set; } = true;
    public bool SuspendOverlayForExcludedWindows { get; set; } = true;
    public string[] FpsExcludedWindowTitleFragments { get; set; } = ["Call of Duty"];
    public string PingTarget { get; set; } = "1.1.1.1";
    public string? PresentMonPath { get; set; }
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

internal sealed record MetricsSnapshot(
    double? Fps,
    double CpuUsage,
    double? CpuTemperature,
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
    public static MetricsSnapshot Empty => new(null, 0, null, 0, null, 0, 0, null, null, "Desktop", "PresentMon fehlt", DateTimeOffset.Now, false);
}

internal sealed class OverlayApplicationContext : ApplicationContext
{
    private readonly OverlaySettings _settings;
    private readonly OverlayForm _form;
    private readonly MetricsSampler _sampler;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _sampling;

    public OverlayApplicationContext()
    {
        _settings = SettingsStore.Load();
        _sampler = new MetricsSampler();
        _form = new OverlayForm(_settings);
        _form.FormClosed += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Overlay anzeigen/verbergen", null, (_, _) => _form.ToggleVisible());
        menu.Items.Add("Klick-Durchlässigkeit umschalten", null, (_, _) => ToggleClickThrough());
        menu.Items.Add("Anti-Cheat-Safe-Modus umschalten", null, (_, _) => ToggleAntiCheatSafeMode());
        menu.Items.Add("Konfiguration öffnen", null, (_, _) => OpenSettings());
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
        _timer.Tick += async (_, _) => await SampleAndRenderAsync();
        _timer.Start();
        _form.Show();
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

    private void OpenSettings()
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
    private readonly FlowLayoutPanel _line;
    private OverlaySettings _settings;
    private MetricsSnapshot _snapshot = MetricsSnapshot.Empty;
    private bool _userVisible = true;
    private bool _compatibilitySuspended;

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

        _line = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        Controls.Add(_line);
        ApplySettings(settings);
        UpdateSnapshot(MetricsSnapshot.Empty);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            if (_settings.ClickThrough) cp.ExStyle |= WsExTransparent;
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

        Opacity = Math.Max(0.05, settings.BackgroundOpacity / 255.0);
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

    public void UpdateSnapshot(MetricsSnapshot snapshot)
    {
        _snapshot = snapshot;
        _line.Controls.Clear();
        AddMetric("FPS", snapshot.Fps is null ? "--" : $"{snapshot.Fps:0}");
        AddMetric("CPU", $"{snapshot.CpuUsage:0}% | {FormatTemperature(snapshot.CpuTemperature)}");
        AddMetric("GPU", $"{snapshot.GpuUsage:0}% | {FormatTemperature(snapshot.GpuTemperature)}");
        AddMetric("NET", $"↓{snapshot.DownloadKibPerSecond:0} ↑{snapshot.UploadKibPerSecond:0} KiB/s");
        AddMetric("LOSS", snapshot.PacketLossPercent is null ? "--" : $"{snapshot.PacketLossPercent:0.0}%");
        Width = _line.PreferredSize.Width + Padding.Horizontal;
        Height = _line.PreferredSize.Height + Padding.Vertical;
        ApplyRoundedRegion();
    }

    private static string FormatTemperature(double? value) => value is null ? "--" : $"{value:0}°C";

    private void AddMetric(string name, string value)
    {
        var chip = new MetricChip($"{name} {value}", ForeColor, Font);
        _line.Controls.Add(chip);
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
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        if (Width < 2 || Height < 2) return;
        Region?.Dispose();
        using var path = RoundedRectangle(new Rectangle(0, 0, Width, Height), _settings.CornerRadius);
        Region = new Region(path);
    }

    private void PlaceOnSelectedScreen()
    {
        var screens = Screen.AllScreens;
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
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
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
    private readonly PresentMonFpsProvider _fps = new();
    private DateTimeOffset _lastTemperatureRead = DateTimeOffset.MinValue;
    private double? _cpuTemperature;
    private double? _gpuTemperature;
    private double _gpuUsage;

    public async Task<MetricsSnapshot> SampleAsync(OverlaySettings settings, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        double cpuUsage = _cpu.ReadPercent();
        var network = _network.ReadRates();
        var target = ForegroundProcess.Get();
        bool excludedBySafeMode = settings.AntiCheatSafeMode && target.Matches(settings.FpsExcludedWindowTitleFragments);
        double? fps;
        if (settings.EnableFpsTelemetry && !excludedBySafeMode)
        {
            await _fps.EnsureProcessAsync(target.ProcessId, settings.PresentMonPath, cancellationToken);
            fps = _fps.ReadFps();
        }
        else
        {
            _fps.Pause(excludedBySafeMode ? "FPS pausiert (Safe-Modus)" : "FPS deaktiviert");
            fps = null;
        }

        if (now - _lastTemperatureRead >= TimeSpan.FromMilliseconds(settings.TemperatureRefreshMilliseconds))
        {
            _lastTemperatureRead = now;
            var gpu = await _gpu.ReadAsync(cancellationToken);
            _gpuUsage = gpu.Usage;
            _gpuTemperature = gpu.Temperature;
            _cpuTemperature = await CpuTemperatureReader.ReadAsync(cancellationToken);
        }

        var ping = await _packetLoss.ReadAsync(settings.PingTarget, cancellationToken);
        return new MetricsSnapshot(fps, cpuUsage, _cpuTemperature, _gpuUsage, _gpuTemperature,
            network.DownloadKibPerSecond, network.UploadKibPerSecond, ping.LossPercent,
            ping.LatencyMilliseconds, target.Name, _fps.Source, now,
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

internal static class CpuTemperatureReader
{
    public static async Task<double?> ReadAsync(CancellationToken cancellationToken)
    {
        const string script = "$t=Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue | Select-Object -ExpandProperty CurrentTemperature; if($t){ (($t | Measure-Object -Average).Average / 10) - 273.15 }";
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double temp)
                ? Math.Clamp(temp, -20, 130)
                : null;
        }
        catch
        {
            return null;
        }
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);
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
            psi.ArgumentList.Add("--no_csv");
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
            Console.WriteLine("PerformanceOverlay self-test: PASS");
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
