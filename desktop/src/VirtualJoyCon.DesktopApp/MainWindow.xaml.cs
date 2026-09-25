using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VirtualJoyCon.Configuration;
using VirtualJoyCon.ControllerModel;
using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.Network.Link;
using VirtualJoyCon.VirtualController;

namespace VirtualJoyCon.DesktopApp;

public partial class MainWindow : Window
{
    private readonly ControllerHub _hub;
    private readonly AppSettings _cfg;
    private readonly Logger _log;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _echoTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly ControllerState _echo = new();
    private JoyConView.ControllerRefs _refs;
    private Dictionary<string, string> _keyToAction = new();
    private UsbAdb.AdbClient? _adb;
    private UsbAdb.AdbWatcher? _watcher;
    private UsbAdb.AdbDaemonLauncher? _daemonLauncher;
    private bool _usdBlink;
    private string _usbStatus = "";
    private bool _suppressStopPrompt;

    public MainWindow()
    {
        InitializeComponent();
        _cfg = App.Settings;
        _log = App.Log;
        _hub = new ControllerHub(_cfg, _log);

        TrySetLogo();
        BuildController();
        BuildKeyLookup();

        ModeLan.IsChecked = true; // LAN local is the always-available default
        foreach (var rb in new[] { ModeUsb, ModeInternet, ModeLan })
            rb.Checked += (_, _) => Dispatcher.InvokeAsync(UpdatePairingVisibility);
        _log.EntryLogged += entry => Dispatcher.BeginInvoke(() =>
        {
            LogList.Items.Add(entry.ToString());
            while (LogList.Items.Count > 600) LogList.Items.RemoveAt(0);
            if (LogList.IsVisible) LogList.ScrollIntoView(LogList.Items[^1]);
        });

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        _echoTimer.Tick += (_, _) => EchoControllerState();
        _echoTimer.Start();

        UpdatePairingVisibility();
        Closed += OnClosed;
        Deactivated += (_, _) => _hub.ReleaseAll(); // alt-tab while holding = release, never stuck
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewKeyUp += Window_PreviewKeyUp;
        Focusable = true;
        Keyboard.Focus(this);
    }

    private void TrySetLogo()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.png", UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.EndInit();
            LogoImg.Source = bmp;
            Icon = bmp;
        }
        catch
        {
            // icon resource optional (generated at release time)
        }
    }

    private void BuildController()
    {
        _refs = JoyConView.Build(_cfg.Interface,
            onButton: (flag, down) => _hub.UiSetButton((ButtonFlags)flag, down),
            onStick: (left, x, y) => _hub.UiSetStick(left, x, y));
        ControllerHost.Content = _refs.Root;
    }

    private void BuildKeyLookup()
    {
        _keyToAction = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, key) in _cfg.KeyMap)
            _keyToAction[key] = action;
    }

    // ==================== status UI ====================

    private void RefreshStatus()
    {
        bool connected = _hub.Connected;
        StatusDot.Fill = connected
            ? (Brush)FindResource("Ok")
            : (_watcher?.LastState == UsbAdb.AdbState.Connected ? (Brush)FindResource("Warn") : (Brush)FindResource("Muted"));
        StatusText.Text = connected
            ? "Android conectado"
            : (_watcher is { LastState: UsbAdb.AdbState.Unauthorized } ? "Aguardando autorização USB no celular" : "Desconectado");

        MethodText.Text = connected
            ? _hub.ActiveTransport switch
            {
                LinkTransport.Usb => "USB (ADB)",
                LinkTransport.Internet => _hub.RelayRouted ? "Internet (relay)" : "Internet (P2P direto)",
                _ => "LAN local (UDP)",
            }
            : (_hub.Links.Mode switch
            {
                Network.NetMode.UsbTcp => "USB — aguardando pareamento",
                Network.NetMode.Internet => "Internet — aguardando pareamento",
                Network.NetMode.Lan => "LAN — aguardando pareamento",
                _ => "—",
            });

        LatencyText.Text = connected
            ? (_hub.HasLatency ? $"{_hub.LatencyMs:F0} ms (RTT)" : "medindo…")
            : "-- ms";
        LossText.Text = connected
            ? (_hub.ActiveTransport == LinkTransport.Internet
                ? $"{_hub.LossPercent:F1}% ({(_hub.DirectP2P ? "P2P" : "relay")})"
                : "n/d (USB/LAN)")
            : "—";
        DeviceText.Text = connected && _hub.DeviceLabel.Length > 0
            ? _hub.DeviceLabel
            : _watcher?.LastDevice is { } d ? d.Model : "—";
        ServiceText.Text = connected
            ? _hub.AndroidServiceState switch
            {
                2 => "Rodando",
                1 => "Iniciando…",
                3 => "ERRO",
                _ => "Parado",
            }
            : "—";
        InputModeText.Text = connected
            ? _hub.AndroidInputMode switch
            {
                2 => "Gamepad global (daemon ADB)",
                3 => "Toque (Acessibilidade)",
                1 => "In-app (teste)",
                _ => "nenhum",
            }
            : "—";
        AndroidVerText.Text = connected ? _hub.AndroidAppVersion : "—";
        VersionText.Text = $"v{_hub.AppVersionText}";

        AdbText.Text = _adb == null ? "—"
            : !_adb.AdbAvailable ? "NÃO ENCONTRADO (instale platform-tools)"
            : _watcher?.LastState switch
            {
                UsbAdb.AdbState.Connected => "CONNECTED",
                UsbAdb.AdbState.Unauthorized => "AGUARDANDO AUTORIZAÇÃO",
                UsbAdb.AdbState.Offline => "OFFLINE",
                _ => "DISCONNECTED",
            };
        AdbDeviceText.Text = _watcher?.LastDevice is { } ad ? $"{ad.Model} ({ad.Serial})" : "—";
        TransportText.Text = connected
            ? _hub.ActiveTransport switch { LinkTransport.Usb => "USB", LinkTransport.Internet => _hub.DirectP2P ? "UDP (P2P)" : "UDP (relay)", _ => "UDP (LAN)" }
            : "—";
        DaemonText.Text = _daemonLauncher?.DaemonRunning == true ? "vjc-daemon ATIVO" : connected && _hub.DaemonReady ? "ATIVO (via app)" : "parado";
        else if (_usdBlink)
        {
            DaemonText.Text = "USB pronto — INICIAR ativa o daemon de entrada";
        }

        PadText.Text = _hub.Gamepad.AnyConnected
            ? string.Join(", ", _hub.Gamepad.Slots.Where(s => s.Connected).Select(s => s.Name))
            : "nenhum";

        var fpsOn = _cfg.Performance.ShowFps;
        FpsText.Text = fpsOn ? $"{_hub.Fps.Fps:F0} fps" : "";

        StartStopButton.Content = _hub.Mode == ControlMode.Running ? "PARAR CONTROLE" : "INICIAR CONTROLE";
        StartStopButton.IsEnabled = connected || _watcher?.LastState == UsbAdb.AdbState.Connected || _hub.Links.Mode != Network.NetMode.Idle;

        // log tail (only while expanded to keep UI cheap)
        if (LogList.IsVisible && LogList.Tag is not string) { }
    }

    private void EchoControllerState()
    {
        _hub.SnapshotTo(_echo);
        foreach (var b in _refs.Buttons)
            b.SetVisualPressed((_echo.Buttons & b.Flag) != 0);
        if (!_refs.Left.IsDragging)
            _refs.Left.SetVisualPosition(_echo.LeftX, _echo.LeftY);
        if (!_refs.Right.IsDragging)
            _refs.Right.SetVisualPosition(_echo.RightX, _echo.RightY);
    }

    // ==================== connect flow ====================

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_hub.Links.Mode != Network.NetMode.Idle && _hub.Links.ReadyLinkCount == 0)
                _hub.Links.Stop();

            if (ModeUsb.IsChecked == true)
            {
                _log.Info("UI", "conexão USB selecionada");
                await StartUsbAsync();
            }
            else if (ModeInternet.IsChecked == true)
            {
                _log.Info("UI", "conexão Internet selecionada");
                StartInternet();
            }
            else
            {
                _log.Info("UI", "conexão LAN local selecionada");
                StartLan();
            }
        }
        catch (Exception ex)
        {
            _log.Error("UI", $"connect: {ex}");
            MessageBox.Show(this, ex.Message, "Virtual Joy-Con — erro de conexão", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StartLan()
    {
        _hub.Links.StartLan(_cfg.Connection.TcpUdpUdpPort, _cfg.Connection.TcpPort);
        var ips = System.Net.Dns.GetHostAddresses(Environment.MachineName)
            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(a))
            .Select(a => a.ToString());
        PairHintText.Text =
            "No celular: VIRTUAL JOY-CON → LAN → busque na rede ou digite o IP do PC e o código.\n" +
            $"PC em: {string.Join(", ", ips)} : {_cfg.Connection.TcpUdpUdpPort} (UDP/TCP)";
        _usbStatus = "";
    }

    private void StartInternet()
    {
        var host = _cfg.Connection.RelayHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show(this,
                "Modo Internet precisa de um relay.\n\n" +
                "Você pode hospedar o VjcRelay (incluído, grátis — basta rodar em qualquer máquina com UDP aberto)\n" +
                "ou conectar PC e celular na mesma rede e usar LAN local.\n\n" +
                "Configure host/porta na aba REDE das configurações.",
                "Virtual Joy-Con");
            OpenSettings(networkTab: true);
            return;
        }
        _hub.Links.StartLan(_cfg.Connection.TcpUdpUdpPort, _cfg.Connection.TcpPort); // also usable locally
        _hub.Links.StartRelay(host, _cfg.Connection.RelayPort, PairingCode.ToWire(_hub.PairingCode));
        PairHintText.Text = $"Relay: {host}:{_cfg.Connection.RelayPort} — sala {_hub.PairingCode}. Se o roteador permitir, o app tenta conexão direta P2P e bypassa o relay.";
    }

    private async Task StartUsbAsync()
    {
        _adb = new UsbAdb.AdbClient(_cfg.Connection.AdbPath);
        if (!_adb.AdbAvailable)
        {
            var r = MessageBox.Show(this,
                "adb (platform-tools) não foi encontrado no sistema.\n\n" +
                "Não instalamos nada silenciosamente. Instale o Android SDK Platform Tools\n" +
                "(winget install Google.PlatformTools) e informe o caminho nas configurações.\n\n" +
                "Abrir instruções de instalação? (navegador)",
                "Virtual Joy-Con — ADB ausente", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://developer.android.com/tools/releases/platform-tools")
                    { UseShellExecute = true });
                }
                catch { }
            }
            OpenSettings(networkTab: false);
            return;
        }

        _watcher = new UsbAdb.AdbWatcher(_adb);
        _watcher.StateChanged += (state, device) => Dispatcher.InvokeAsync(() =>
        {
            _log.Info("ADB", $"estado: {state} {device?.Model}");
            if (state == UsbAdb.AdbState.Connected) _usdBlink = true;
            RefreshStatus();
        });
        _watcher.Start();

        var devices = await _adb.ListDevicesAsync();
        var dev = devices.FirstOrDefault(d => d.State == "device");
        if (dev == null)
        {
            dev = devices.FirstOrDefault();
            _usbStatus = dev == null
                ? "Nenhum dispositivo ADB. Conecte o cabo e ative a Depuração USB."
                : $"Dispositivo {dev.Model}: estado '{dev.State}' — autorize o RSA no celular.";
            _log.Warn("ADB", _usbStatus);
            _hub.Links.StartLan(_cfg.Connection.TcpUdpUdpPort, _cfg.Connection.TcpPort);
            PairHintText.Text = _usbStatus;
            return;
        }

        // Device authorized: TCP listener + adb reverse; daemon starts with INICIAR CONTROLE.
        _hub.Links.StartLan(_cfg.Connection.TcpUdpUdpPort, _cfg.Connection.TcpPort);
        var (revExit, _, revErr) = await _adb.SetReverseAsync(dev.Serial, _cfg.Connection.TcpPort);
        _usdBlink = false;
        if (revExit != 0)
        {
            _log.Warn("ADB", $"reverse falhou: {revErr}");
            PairHintText.Text = "ADB conectado, mas 'adb reverse' falhou. Use LAN informando o IP do PC.";
            return;
        }
        PairHintText.Text =
            $"Android {dev.Model} conectado via ADB.\n" +
            "Toque em INICIAR CONTROLE para subir o serviço no celular (o app abre automaticamente se instalado).";
        _log.Info("ADB", $"dispositivo pronto: {dev.Model} [{dev.Serial}]");
        // Ask the companion app to wake up via the same reverse channel
        try { await _adb.RunAsync($"-s {dev.Serial} shell monkey -p com.joyliveex.vjc -c android.intent.category.LAUNCHER 1", 4000); } catch { }
    }

    private static class PairingCode
    {
        public static string ToWire(string display) => Network.Crypto.PairingCode.ToWireCode(display);
    }

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_hub.Mode == ControlMode.Running)
        {
            _hub.SetMode(ControlMode.Stopped);
            if (_daemonLauncher != null) await _daemonLauncher.StopAsync();
            return;
        }
        _hub.SetMode(ControlMode.Running);

        // USB/adb daemon start (only when we have an authorized device + jar available)
        var dev = _watcher?.LastDevice;
        if (dev != null && _adb is { AdbAvailable: true } && _cfg.Connection.StartDaemonOnUsb)
        {
            var jar = Path.Combine(AppContext.BaseDirectory, "vjc-daemon.jar");
            if (File.Exists(jar))
            {
                _daemonLauncher ??= new UsbAdb.AdbDaemonLauncher(_adb);
                _daemonLauncher.DaemonLog += line => Dispatcher.InvokeAsync(() => _log.Info("DAEMON", line));
                var err = await _daemonLauncher.StartAsync(dev.Serial, _cfg.Connection.TcpPort, _hub.PairingCode, jar);
                if (err != null) _log.Warn("DAEMON", err);
            }
            else
            {
                _log.Warn("DAEMON", "vjc-daemon.jar não está ao lado do EXE — injeção global indisponível; o app usará modo local.");
            }
        }

        // Tell every ready peer (the companion app) to start its service.
        _hub.Links.SendCmdToAll(Network.Protocol.Wire.CmdStartDaemon, Array.Empty<byte>());
        Keyboard.Focus(this);
    }

    // ==================== keyboard ====================

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (TryHandleKey(e.Key, true)) e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (TryHandleKey(e.Key, false)) e.Handled = true;
    }

    private bool TryHandleKey(Key key, bool down)
    {
        var name = key.ToString();
        if (!_keyToAction.TryGetValue(name, out var action)) return false;
        if (action == "ToggleControl")
        {
            if (down)
                StartStopButton_Click(this, new RoutedEventArgs());
            return true;
        }
        _hub.SetAction(action, down);
        return true;
    }

    // ==================== misc UI ====================

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_hub.PairingCode); } catch { }
    }

    private void NewCode_Click(object sender, RoutedEventArgs e)
    {
        _hub.RegeneratePairingCode();
        UpdatePairingVisibility();
    }

    private void UpdatePairingVisibility()
    {
        PairCodeText.Text = _hub.PairingCode;
        PairingCard.Visibility = ModeUsb.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(_cfg.Directory, "logs")) { UseShellExecute = true });
        }
        catch { }
    }

    public void OpenSettings(bool networkTab = false)
    {
        var w = new SettingsWindow(_cfg, _hub, networkTab) { Owner = this };
        w.SettingsSaved += () =>
        {
            BuildKeyLookup();
            BuildController();
            _hub.ApplySettings();
        };
        w.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _echoTimer.Stop();
        _suppressStopPrompt = true;
        _hub.SetMode(ControlMode.Stopped);
        _watcher?.Dispose();
        _hub.Dispose();
        try { _cfg.Save(); } catch { }
    }

    /// <summary>Called by the wizard after choosing a connection mode.</summary>
    public void ApplyWizardChoice(string mode)
    {
        switch (mode)
        {
            case "usb": ModeUsb.IsChecked = true; ConnectButton_Click(this, new RoutedEventArgs()); break;
            case "internet": ModeInternet.IsChecked = true; break;
            default: ModeLan.IsChecked = true; break;
        }
    }
}
