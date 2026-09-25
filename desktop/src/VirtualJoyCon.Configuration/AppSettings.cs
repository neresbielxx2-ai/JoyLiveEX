using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualJoyCon.Configuration;

/// <summary>
/// Root settings object. Persisted as JSON under %APPDATA%\JoyLiveEX\VirtualJoyCon\settings.json.
/// Atomic save (tmp + move) so a crash can never corrupt user config.
/// </summary>
public sealed class AppSettings
{
    public const string AppFolderName = "JoyLiveEX";
    public const string AppFileName = "VirtualJoyCon";

    public int ConfigVersion { get; set; } = 1;
    public bool FirstRunDone { get; set; }

    public ConnectionSettings Connection { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public InterfaceSettings Interface { get; set; } = new();
    public AndroidOptions Android { get; set; } = new();

    public StickTuning LeftStick { get; set; } = new();
    public StickTuning RightStick { get; set; } = new();

    /// <summary>action name -> key literal (WPF Key enum name). See KeyboardMap for defaults.</summary>
    public Dictionary<string, string> KeyMap { get; set; } = new();

    /// <summary>action name -> physical gamepad button index (XInput mask).</summary>
    public Dictionary<string, int> GamepadMap { get; set; } = new();
    public string PreferredGamepad { get; set; } = "";

    [JsonIgnore]
    public string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, AppFileName);

    [JsonIgnore]
    public string FilePath => Path.Combine(Directory, "settings.json");

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings LoadOrCreate()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName, AppFileName);
            var file = Path.Combine(dir, "settings.json");
            if (File.Exists(file))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOpts);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Corrupt file -> fall through to defaults (original is kept as settings.json.bak next save).
        }
        var s = new AppSettings();
        if (s.KeyMap.Count == 0) KeyboardMap.ApplyDefaults(s.KeyMap);
        if (s.GamepadMap.Count == 0) GamepadMapDefaults.Apply(s.GamepadMap);
        return s;
    }

    public void Save()
    {
        Directory.CreateDirectory(Directory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
        if (File.Exists(FilePath))
        {
            try { File.Replace(tmp, FilePath, FilePath + ".bak"); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
