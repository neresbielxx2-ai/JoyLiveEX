using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace VirtualJoyCon.DesktopApp;

public partial class App : Application
{
    public static Configuration.AppSettings Settings = null!;
    public static Diagnostics.Logger Log = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = Configuration.AppSettings.LoadOrCreate();
        Log = new Diagnostics.Logger(Path.Combine(Settings.Directory, "logs"));
        Log.Info("APP", "Virtual Joy-Con iniciando (JoyLiveEX)");

        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("APP", $"unhandled: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Log.Warn("APP", $"task: {args.Exception?.Message}");

        ThemeManager.Apply(Settings.Interface.DarkMode);

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        if (!Settings.FirstRunDone)
        {
            var w = new WizardWindow { Owner = main };
            w.ShowDialog();
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("APP", e.Exception.ToString());
        MessageBox.Show(
            "Ocorreu um erro inesperado.\n\n" + e.Exception.Message +
            "\n\nDetalhes em: " + Path.Combine(Settings.Directory, "logs", "session.log"),
            "Virtual Joy-Con", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Settings.Save(); } catch { }
        Log.Info("APP", "encerrado");
        Log.Dispose();
        base.OnExit(e);
    }
}

public static class ThemeManager
{
    public static void Apply(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < dicts.Count; i++)
            if (dicts[i].Source != null && dicts[i].Source.OriginalString.Contains("Theme/"))
            {
                dicts[i] = new ResourceDictionary { Source = new Uri($"Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
                return;
            }
        dicts.Insert(0, new ResourceDictionary { Source = new Uri($"Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) });
    }
}
