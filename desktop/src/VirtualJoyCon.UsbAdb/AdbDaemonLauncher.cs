using System.Diagnostics;
using System.Security.Cryptography;

namespace VirtualJoyCon.UsbAdb;

/// <summary>
/// Pushes the VJC input daemon (DEX jar, see android/daemon) to /data/local/tmp
/// and runs it as the *shell* uid via app_process — the same proven architecture
/// scrcpy/DeskDock use. Shell already holds the privileges needed to inject
/// input system-wide (android.permission.INJECT_EVENTS), which an ordinary
/// installed APK cannot have. The daemon connects back to the PC through
/// `adb reverse`, so no port/IP configuration is needed on the phone.
/// </summary>
public sealed class AdbDaemonLauncher : IDisposable
{
    public const string DeviceJarPath = "/data/local/tmp/vjc-daemon.jar";
    public const string MainClass = "com.joyliveex.vjc.daemon.Main";

    private readonly AdbClient _adb;
    private Process? _daemonProc;
    private string? _daemonSerial;
    private int _reversePort;
    private readonly object _gate = new();

    public event Action<string>? DaemonLog;

    public bool DaemonRunning { get { lock (_gate) return _daemonProc is { HasExited: false }; } }
    public string? DaemonDevice => _daemonSerial;

    public AdbDaemonLauncher(AdbClient adb) => _adb = adb;

    /// <summary>
    /// Full USB start sequence: reverse port → push jar (only when hash differs) →
    /// spawn daemon. Returns a human-readable failure reason, or null on success.
    /// </summary>
    public async Task<string?> StartAsync(string serial, int pcTcpPort, string pairingCode, string daemonJarPath, CancellationToken tok = default)
    {
        if (!_adb.AdbAvailable) return "adb não encontrado";
        if (!File.Exists(daemonJarPath)) return $"daemon jar ausente: {daemonJarPath}";

        // 1. adb reverse: a device connection to 127.0.0.1:pcTcpPort arrives at our TCP listener.
        var (revExit, _, revErr) = await _adb.RunAsync($"-s {serial} reverse tcp:{pcTcpPort} tcp:{pcTcpPort}", 6000, tok);
        if (revExit != 0) return $"adb reverse falhou: {revErr}";

        // 2. push daemon jar if missing or changed
        var (mdExit, mdOut, _) = await _adb.RunAsync($"-s {serial} shell md5sum {DeviceJarPath}", 6000, tok);
        string localMd5;
        using (var fs = File.OpenRead(daemonJarPath))
            localMd5 = Convert.ToHexString(MD5.HashData(fs)).ToLowerInvariant();
        bool needsPush = mdExit != 0 || !mdOut.Contains(localMd5, StringComparison.OrdinalIgnoreCase);
        if (needsPush)
        {
            DaemonLog?.Invoke("enviando vjc-daemon.jar para o dispositivo…");
            var (pushExit, _, pushErr) = await _adb.RunAsync($"-s {serial} push \"{daemonJarPath}\" {DeviceJarPath}", 30000, tok);
            if (pushExit != 0) return $"adb push falhou: {pushErr}";
        }

        // 3. stop any previous daemon, then start it (shell uid, app_process, dex jar)
        await StopDaemonProcessAsync(serial, tok);
        var args = $"-s {serial} shell CLASSPATH={DeviceJarPath} app_process / {MainClass} {pcTcpPort} {pairingCode}";
        var psi = new ProcessStartInfo(_adb.AdbPath!, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            var p = Process.Start(psi);
            if (p == null) return "não foi possível iniciar o adb shell";
            lock (_gate)
            {
                _daemonProc?.Dispose();
                _daemonProc = p;
                _daemonSerial = serial;
                _reversePort = pcTcpPort;
            }
            _ = PumpAsync(p.StandardOutput);
            _ = PumpAsync(p.StandardError);
            return null;
        }
        catch (Exception ex)
        {
            return $"falha ao iniciar daemon: {ex.Message}";
        }
    }

    private async Task PumpAsync(StreamReader reader)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.Length > 0) DaemonLog?.Invoke(line.Trim());
            }
        }
        catch { }
    }

    public async Task StopAsync()
    {
        string? serial;
        Process? p;
        int port;
        lock (_gate)
        {
            serial = _daemonSerial;
            p = _daemonProc;
            port = _reversePort;
            _daemonProc = null;
            _daemonSerial = null;
        }
        if (p != null)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            p.Dispose();
        }
        if (serial != null)
        {
            await StopDaemonProcessAsync(serial).ConfigureAwait(false);
            if (port > 0)
                try { await _adb.RemoveReverseAsync(serial, port).ConfigureAwait(false); } catch { }
        }
    }

    private async Task StopDaemonProcessAsync(string serial, CancellationToken tok = default)
    {
        try
        {
            // pkill exists on Android's toybox (API 23+)
            await _adb.RunAsync($"-s {serial} shell pkill -f {MainClass}", 5000, tok).ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose() => _ = StopAsync();
}
