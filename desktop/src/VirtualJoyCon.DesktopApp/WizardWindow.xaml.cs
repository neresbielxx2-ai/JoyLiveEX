using System.Windows;
using System.Windows.Media.Imaging;

namespace VirtualJoyCon.DesktopApp;

public partial class WizardWindow : Window
{
    private readonly UsbAdb.AdbClient _adbProbe = new(App.Settings.Connection.AdbPath);

    public WizardWindow()
    {
        InitializeComponent();
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.png", UriKind.Absolute);
            WizardLogo.Source = new BitmapImage(uri);
        }
        catch { }

        // Section 19 UX: detect USB automatically and *suggest* it when a device is there.
        _ = DetectAsync();
    }

    private async Task DetectAsync()
    {
        try
        {
            if (!_adbProbe.AdbAvailable) return;
            var devices = await _adbProbe.ListDevicesAsync();
            var dev = devices.FirstOrDefault(d => d.State == "device");
            if (dev == null) return;
            await Dispatcher.InvokeAsync(() =>
            {
                UsbHint.Text = $"Android detectado via ADB: {dev.Model} → USB recomendado";
                UsbButton.Focus();
            });
        }
        catch { }
    }

    private void Usb_Click(object sender, RoutedEventArgs e) => Finish("usb");
    private void Net_Click(object sender, RoutedEventArgs e) => Finish("net");

    private void Finish(string choice)
    {
        if (DontShow.IsChecked == true)
            App.Settings.FirstRunDone = true;
        App.Settings.Save();
        if (Owner is MainWindow mw)
            mw.ApplyWizardChoice(choice);
        Close();
    }
}
