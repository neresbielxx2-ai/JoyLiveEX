using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VirtualJoyCon.Configuration;
using VirtualJoyCon.VirtualController;

namespace VirtualJoyCon.DesktopApp;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _cfg;
    private readonly ControllerHub _hub;
    private string? _capturingAction;
    private readonly DispatcherTimer _padCaptureTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private ushort _padBaseline;

    public event Action? SettingsSaved;

    public SettingsWindow(AppSettings cfg, ControllerHub hub, bool openNetworkTab = false)
    {
        InitializeComponent();
        _cfg = cfg;
        _hub = hub;
        Load();
        if (openNetworkTab) Tabs.SelectedIndex = 2;

        Ldz.ValueChanged += (_, _) => LdzVal.Text = Ldz.Value.ToString("0.00");
        Lsens.ValueChanged += (_, _) => LsensVal.Text = Lsens.Value.ToString("0.00");
        Lmax.ValueChanged += (_, _) => LmaxVal.Text = Lmax.Value.ToString("0.00");
        Lexpo.ValueChanged += (_, _) => LexpoVal.Text = Lexpo.Value.ToString("0.0");
        Rdz.ValueChanged += (_, _) => RdzVal.Text = Rdz.Value.ToString("0.00");
        Rsens.ValueChanged += (_, _) => RsensVal.Text = Rsens.Value.ToString("0.00");
        UiScale.ValueChanged += (_, _) => UiScaleVal.Text = UiScale.Value.ToString("0.00");
        Opacity.ValueChanged += (_, _) => OpacityVal.Text = Opacity.Value.ToString("0.00");
        Spacing.ValueChanged += (_, _) => SpacingVal.Text = ((int)Spacing.Value).ToString();

        PreviewKeyDown += Settings_PreviewKeyDown;
        _padCaptureTimer.Tick += (_, _) => PollPadCapture();
    }

    private void Load()
    {
        Ldz.Value = _cfg.LeftStick.Deadzone;
        Lsens.Value = _cfg.LeftStick.Sensitivity;
        Lmax.Value = _cfg.LeftStick.MaxIntensity;
        Lcurve.SelectedIndex = Math.Clamp(_cfg.LeftStick.Curve, 0, 3);
        Lexpo.Value = _cfg.LeftStick.CurveExponent;
        Linvert.IsChecked = _cfg.LeftStick.InvertY;
        Rdz.Value = _cfg.RightStick.Deadzone;
        Rsens.Value = _cfg.RightStick.Sensitivity;
        Rcurve.SelectedIndex = Math.Clamp(_cfg.RightStick.Curve, 0, 3);
        Rinvert.IsChecked = _cfg.RightStick.InvertY;

        VibEn.IsChecked = _cfg.Android.VibrationEnabled;
        VibMs.Text = _cfg.Android.VibrationMs.ToString();

        AdbPathBox.Text = _cfg.Connection.AdbPath;
        StartDaemonChk.IsChecked = _cfg.Connection.StartDaemonOnUsb;
        AutoReconnect.IsChecked = _cfg.Connection.AutoReconnect;
        TimeoutBox.Text = _cfg.Connection.ReconnectTimeoutSec.ToString();

        UseRelay.IsChecked = _cfg.Connection.UseRelay;
        RelayHost.Text = _cfg.Connection.RelayHost;
        RelayPort.Text = _cfg.Connection.RelayPort.ToString();
        PreferP2P.IsChecked = _cfg.Connection.PreferP2P;
        UdpPort.Text = _cfg.Connection.TcpUdpUdpPort.ToString();
        TcpPort.Text = _cfg.Connection.TcpPort.ToString();

        UiScale.Value = _cfg.Interface.UiScale;
        Opacity.Value = _cfg.Interface.Opacity;
        Spacing.Value = _cfg.Interface.RailSpacing;
        ThemeBox.SelectedIndex = _cfg.Interface.DarkMode ? 0 : 1;
        ShowLabels.IsChecked = _cfg.Interface.ShowKeyLabels;

        PollHz.SelectedIndex = _cfg.Performance.InputPollHz switch { <= 60 => 0, <= 120 => 1, <= 250 => 2, _ => 3 };
        NetHz.SelectedIndex = _cfg.Performance.NetworkSendHz switch { <= 30 => 0, <= 60 => 1, <= 125 => 2, _ => 3 };
        ShowFps.IsChecked = _cfg.Performance.ShowFps;
        Verbose.IsChecked = _cfg.Performance.VerboseDiagnostics;

        RefreshKeyList();
        RefreshAndroid();
    }

    private void RefreshAndroid()
    {
        DevModel.Text = _hub.DeviceLabel.Length > 0 ? _hub.DeviceLabel : "— (conecte o Android)";
        DevSdk.Text = _hub.AndroidSdk > 0 ? _hub.AndroidSdk.ToString() : "—";
        DevAdb.Text = _hub.ActiveTransport switch
        {
            Network.Link.LinkTransport.Usb => "CONNECTED (USB)",
            Network.Link.LinkTransport.Internet => "pareado via relay/P2P",
            _ => _hub.Connected ? "pareado via LAN" : "—",
        };
        DevInput.Text = _hub.AndroidInputMode switch
        {
            2 => "GAMEPAD GLOBAL (daemon ADB) ✔",
            3 => "TOQUE via Acessibilidade",
            1 => "IN-APP (apenas testes)",
            _ => "—",
        };
    }

    // ==================== key remap ====================

    private void RefreshKeyList()
    {
        var sel = KeyList.SelectedItem as string;
        KeyList.Items.Clear();
        foreach (var action in KeyboardMap.RemappableActions)
        {
            var key = _cfg.KeyMap.TryGetValue(action, out var k) ? k : "—";
            KeyList.Items.Add($"{action}\t{key}");
        }
        if (sel != null)
        {
            var i = KeyList.Items.Cast<string>().ToList().FindIndex(x => x.StartsWith(sel.Split('\t')[0] + "\t"));
            if (i >= 0) KeyList.SelectedIndex = i;
        }
        KeyList.MouseDoubleClick += (_, _) => StartCapture();
        KeyList.KeyDown += (s, e) => { if (e.Key == Key.Enter) StartCapture(); };
    }

    private void StartCapture()
    {
        if (KeyList.SelectedItem is not string item) { CaptureLabel.Text = "selecione uma ação primeiro"; return; }
        _capturingAction = item.Split('\t')[0];
        CaptureLabel.Text = $"capturando: {_capturingAction} — pressione uma TECLA ou botão do GAMEPAD…";
        CaptureLabel.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        _padBaseline = _hub.Gamepad.RawButtonsLast;
        _padCaptureTimer.Start();
    }

    private void StopCapture(string? done)
    {
        _capturingAction = null;
        _padCaptureTimer.Stop();
        CaptureLabel.Foreground = (System.Windows.Media.Brush)FindResource("Text");
        CaptureLabel.Text = done == null ? "pronto" : $" {_capturingAction} → {done}";
        if (done != null) CaptureLabel.Text = $"capturado ✔";
        RefreshKeyList();
    }

    private void Settings_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingAction == null) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { StopCapture(null); return; }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        _cfg.KeyMap[_capturingAction] = e.Key.ToString();
        StopCapture(e.Key.ToString());
    }

    private void PollPadCapture()
    {
        if (_capturingAction == null) return;
        var raw = _hub.Gamepad.RawButtonsLast;
        var pressed = raw & (ushort)~_padBaseline;
        if (pressed == 0) return;
        _cfg.GamepadMap[_capturingAction] = pressed;
        StopCapture($"gamepad 0x{pressed:X4}");
    }

    private void ResetKeys_Click(object sender, RoutedEventArgs e)
    {
        KeyboardMap.ApplyDefaults(_cfg.KeyMap);
        RefreshKeyList();
    }

    private void ClearPad_Click(object sender, RoutedEventArgs e)
    {
        _cfg.GamepadMap.Clear();
        GamepadMapDefaults.Apply(_cfg.GamepadMap);
        CaptureLabel.Text = "mapeamento do gamepad restaurado para o padrão XInput";
    }

    // ==================== adb / daemon ====================

    private void FindAdb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta do platform-tools (contém adb.exe)" };
            if (dlg.ShowDialog(this) == true)
                AdbPathBox.Text = dlg.FolderName;
        }
        catch
        {
            AdbPathBox.Text = @"C:\platform-tools\adb.exe";
        }
    }

    private async void TestAdb_Click(object sender, RoutedEventArgs e)
    {
        AdbTestResult.Text = "verificando…";
        try
        {
            var adb = new UsbAdb.AdbClient(AdbPathBox.Text);
            if (!adb.AdbAvailable)
            {
                AdbTestResult.Text = "adb NÃO encontrado. Instale o Android platform-tools e informe o caminho (ou deixe vazio para usar o PATH). Nada é instalado automaticamente.";
                return;
            }
            var (exit, stdout, stderr) = await adb.RunAsync("version");
            var devices = await adb.ListDevicesAsync();
            var status = devices.Count == 0 ? "nenhum dispositivo" : string.Join(", ", devices.Select(d => $"{d.Model} [{d.State}]"));
            AdbTestResult.Text = $"OK: {stdout.Split('\n')[0].Trim()} — {status}{(exit != 0 ? " / stderr: " + stderr : "")}";
        }
        catch (Exception ex)
        {
            AdbTestResult.Text = "erro: " + ex.Message;
        }
    }

    private void RestartDaemon_Click(object sender, RoutedEventArgs e)
    {
        _hub.Links.SendCmdToAll(Network.Protocol.Wire.CmdStartDaemon, Array.Empty<byte>());
        AdbTestResult.Text = "comando de reinício enviado aos peers conectados";
    }

    private void PushConfig_Click(object sender, RoutedEventArgs e)
    {
        _hub.ApplySettings();
        AdbTestResult.Text = "configuração enviada";
    }

    // ==================== apply ====================

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        _cfg.LeftStick.Deadzone = (float)Ldz.Value;
        _cfg.LeftStick.Sensitivity = (float)Lsens.Value;
        _cfg.LeftStick.MaxIntensity = (float)Lmax.Value;
        _cfg.LeftStick.Curve = Lcurve.SelectedIndex;
        _cfg.LeftStick.CurveExponent = (float)Lexpo.Value;
        _cfg.LeftStick.InvertY = Linvert.IsChecked == true;
        _cfg.RightStick.Deadzone = (float)Rdz.Value;
        _cfg.RightStick.Sensitivity = (float)Rsens.Value;
        _cfg.RightStick.Curve = Rcurve.SelectedIndex;
        _cfg.RightStick.InvertY = Rinvert.IsChecked == true;

        _cfg.Android.VibrationEnabled = VibEn.IsChecked == true;
        if (int.TryParse(VibMs.Text, out var vib)) _cfg.Android.VibrationMs = Math.Clamp(vib, 10, 2000);

        _cfg.Connection.AdbPath = AdbPathBox.Text.Trim();
        _cfg.Connection.StartDaemonOnUsb = StartDaemonChk.IsChecked == true;
        _cfg.Connection.AutoReconnect = AutoReconnect.IsChecked == true;
        if (int.TryParse(TimeoutBox.Text, out var to)) _cfg.Connection.ReconnectTimeoutSec = Math.Clamp(to, 3, 120);

        _cfg.Connection.UseRelay = UseRelay.IsChecked == true;
        _cfg.Connection.RelayHost = RelayHost.Text.Trim();
        if (int.TryParse(RelayPort.Text, out var rp)) _cfg.Connection.RelayPort = Math.Clamp(rp, 1, 65535);
        _cfg.Connection.PreferP2P = PreferP2P.IsChecked == true;
        if (int.TryParse(UdpPort.Text, out var up)) _cfg.Connection.TcpUdpUdpPort = Math.Clamp(up, 1024, 65535);
        if (int.TryParse(TcpPort.Text, out var tp2)) _cfg.Connection.TcpPort = Math.Clamp(tp2, 1024, 65535);

        _cfg.Interface.UiScale = UiScale.Value;
        _cfg.Interface.Opacity = Opacity.Value;
        _cfg.Interface.RailSpacing = Spacing.Value;
        var wasDark = _cfg.Interface.DarkMode;
        _cfg.Interface.DarkMode = ThemeBox.SelectedIndex == 0;
        _cfg.Interface.ShowKeyLabels = ShowLabels.IsChecked == true;

        _cfg.Performance.InputPollHz = new[] { 60, 120, 250, 500 }[PollHz.SelectedIndex];
        _cfg.Performance.NetworkSendHz = new[] { 30, 60, 125, 250 }[NetHz.SelectedIndex];
        _cfg.Performance.ShowFps = ShowFps.IsChecked == true;
        _cfg.Performance.VerboseDiagnostics = Verbose.IsChecked == true;
        App.Log.Verbose = _cfg.Performance.VerboseDiagnostics;

        try { _cfg.Save(); } catch (Exception ex) { MessageBox.Show(this, "erro ao salvar: " + ex.Message); }
        if (wasDark != _cfg.Interface.DarkMode) ThemeManager.Apply(_cfg.Interface.DarkMode);
        SettingsSaved?.Invoke();
        Close();
    }
}
