namespace VirtualJoyCon.UsbAdb;

public enum AdbState
{
    NoAdb,            // adb executable not found
    ServerError,      // adb server failed to start
    WaitingDevice,    // no device / unauthorized
    Unauthorized,     // "unauthorized" — accept the RSA dialog on the phone
    Offline,          // device offline
    Connected,        // one or more devices authorized
}

public sealed record AdbDevice(string Serial, string Model, string State)
{
    public bool Usb => !Serial.Contains(':');
}

/// <summary>
/// Thin wrapper around the user's own platform-tools (adb.exe).
/// DESIGN RULE (spec §8): we NEVER download or install anything silently. If adb
/// is missing, the app shows instructions and an "open install docs" action; the
/// user installs platform-tools themselves. Detection order: settings override →
/// PATH → common install locations.
/// </summary>
public sealed class AdbClient
{
    private readonly string? _adbPath;
    public string? AdbPath => _adbPath;
    public bool AdbAvailable => _adbPath != null;

    public AdbClient(string? configuredPath = null)
    {
        _adbPath = Resolve(configuredPath);
    }

    public static string? Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var p = configured.EndsWith("adb.exe", StringComparison.OrdinalIgnoreCase) ? configured : Path.Combine(configured, "adb.exe");
            if (File.Exists(p)) return p;
            if (File.Exists(configured)) return configured;
        }
        // PATH
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try
            {
                var cand = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "adb.exe" : "adb");
                if (File.Exists(cand)) return cand;
            }
            catch { }
        }
        // common locations
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var cand in new[]
        {
            Path.Combine(local, "Android", "platform-tools", "adb.exe"),
            Path.Combine(pf, "Android", "platform-tools", "adb.exe"),
            Path.Combine(pf, "Android", "Android SDK", "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "platform-tools", "adb.exe"),
        })
        {
            if (File.Exists(cand)) return cand;
        }
        return null;
    }

    public async Task<(int exit, string stdout, string stderr)> RunAsync(string args, int timeoutMs = 8000, CancellationToken tok = default)
    {
        if (_adbPath == null) throw new InvalidOperationException("adb not found");
        var psi = new System.Diagnostics.ProcessStartInfo(_adbPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var outT = p.StandardOutput.ReadToEndAsync();
        var errT = p.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(tok);
        timeout.CancelAfter(timeoutMs);
        try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return (-1, "", "adb timeout");
        }
        return (p.ExitCode, await outT, await errT);
    }

    /// <summary>Parses `adb devices -l`. Raw state string per device.</summary>
    public async Task<List<AdbDevice>> ListDevicesAsync()
    {
        var result = new List<AdbDevice>();
        if (_adbPath == null) return result;
        try
        {
            await RunAsync("start-server", 6000).ConfigureAwait(false);
            var (exit, stdout, _) = await RunAsync("devices -l", 6000).ConfigureAwait(false);
            if (exit != 0) return result;
            foreach (var rawLine in stdout.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("*") || line.StartsWith("List of")) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var serial = parts[0];
                var state = parts[1];
                string model = "";
                foreach (var tokPair in parts)
                {
                    if (tokPair.StartsWith("model:")) { model = tokPair[6..]; break; }
                }
                result.Add(new AdbDevice(serial, model.Replace('_', ' '), state));
            }
        }
        catch { }
        return result;
    }

    public async Task<string> GetPropAsync(string serial, string prop)
    {
        if (_adbPath == null) return "";
        try
        {
            var (_, stdout, _) = await RunAsync($"-s {serial} shell getprop {prop}", 5000).ConfigureAwait(false);
            return stdout.Trim();
        }
        catch { return ""; }
    }

    /// <summary>`adb reverse tcp:X tcp:X` — device connections to 127.0.0.1:X reach the PC.</summary>
    public Task<(int exit, string stdout, string stderr)> SetReverseAsync(string serial, int port)
        => RunAsync($"-s {serial} reverse tcp:{port} tcp:{port}");

    public Task<(int exit, string stdout, string stderr)> RemoveReverseAsync(string serial, int port)
        => RunAsync($"-s {serial} reverse --remove tcp:{port}");

    public Task<(int exit, string stdout, string stderr)> PushAsync(string serial, string local, string remote)
        => RunAsync($"-s {serial} push \"{local}\" \"{remote}\"", 30000);

    public Task<(int exit, string stdout, string stderr)> Md5Async(string serial, string remote)
        => RunAsync($"-s {serial} shell md5sum \"{remote}\"", 8000);
}
