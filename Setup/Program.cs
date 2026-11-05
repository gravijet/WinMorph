using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinMorphSetup
{
    /// <summary>Installs WinMorph for the current user (no admin needed) into %LOCALAPPDATA%\Programs\WinMorph.</summary>
    public static class Program
    {
        static readonly bool De = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        static string L(string de, string en) => De ? de : en;
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinMorph");
        static string Exe => Path.Combine(Dir, "WinMorph.exe");

        [STAThread]
        public static int Main(string[] args)
        {
            // /S = silent install (shortcuts + autostart, no look applied)
            if (args.Contains("/S", StringComparer.OrdinalIgnoreCase)) { try { Install(true, false, null); return 0; } catch { return 1; } }
            return new Application().Run(new SetupWindow());
        }

        /// <summary>Stops a running WinMorph, copies the files, lets WinMorph finish (shortcuts, uninstall entry, autostart).</summary>
        public static void Install(bool desktop, bool applyHacker, Action<string> progress)
        {
            progress?.Invoke(L("Laufendes WinMorph wird beendet …", "Stopping running WinMorph …"));
            if (File.Exists(Exe)) { Run(Exe, "--shell-stop", 15000); Run(Exe, "--live-stop", 10000); }
            foreach (var p in Process.GetProcessesByName("WinMorph"))
            {
                try { if (string.Equals(p.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase)) { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); } } catch { }
                p.Dispose();
            }
            progress?.Invoke(L("Dateien werden kopiert …", "Copying files …"));
            Directory.CreateDirectory(Dir);
            foreach (var name in new[] { "WinMorph.exe", "WinMorph.exe.config", "LICENSE" })
            {
                var target = Path.Combine(Dir, name);
                for (int i = 0; ; i++)
                {
                    try
                    {
                        using (var src = typeof(Program).Assembly.GetManifestResourceStream(name))
                        using (var dst = File.Create(target)) src.CopyTo(dst);
                        break;
                    }
                    catch (IOException) when (i < 10) { Thread.Sleep(500); }   // the old file can stay locked for a moment
                }
            }
            progress?.Invoke(L("Verknüpfungen und Autostart werden eingerichtet …", "Setting up shortcuts and autostart …"));
            Run(Exe, "--post-install" + (desktop ? " desktop" : ""), 60000);
            if (applyHacker)
            {
                progress?.Invoke(L("Hacker-Look wird angewendet …", "Applying the Hacker look …"));
                Run(Exe, "--apply terminal", 180000);
            }
        }

        static void Run(string exe, string args, int waitMs)
        {
            try { using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false })) p.WaitForExit(waitMs); } catch { }
        }

        public static void Launch() { try { Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true }); } catch { } }
    }

    sealed class SetupWindow : Window
    {
        static readonly bool De = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        static string L(string de, string en) => De ? de : en;
        static SolidColorBrush B(uint rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

        public SetupWindow()
        {
            Title = L("WinMorph installieren", "Install WinMorph");
            Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = B(0x0C0E12); UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            ImageSource icon = null;
            try { icon = BitmapFrame.Create(new Uri("pack://application:,,,/WinMorph.ico")); Icon = icon; } catch { }
            var root = new StackPanel { Margin = new Thickness(32, 28, 32, 26) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Image { Source = icon, Width = 48, Height = 48 });
            var ht = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ht.Children.Add(new TextBlock { Text = "WinMorph", FontSize = 26, FontWeight = FontWeights.Bold, Foreground = B(0xF2F4F8), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
            ht.Children.Add(new TextBlock { Text = L("Version 5.1 · Windows-Looks per Klick", "Version 5.1 · Windows looks in one click"), FontSize = 12.5, Foreground = B(0x9AA3B2) });
            head.Children.Add(ht);
            root.Children.Add(head);
            root.Children.Add(new TextBlock { Text = L("WinMorph wird nur für dich installiert – ohne Administratorrechte. Alles lässt sich jederzeit über „Original wiederherstellen“ oder die Deinstallation zurücksetzen.", "WinMorph is installed just for you – no admin rights needed. Everything can be undone any time with “Restore original” or by uninstalling."), TextWrapping = TextWrapping.Wrap, Foreground = B(0x9AA3B2), FontSize = 13, Margin = new Thickness(0, 20, 0, 14), LineHeight = 19 });
            root.Children.Add(new TextBlock { Text = L("Ziel: ", "Target: ") + Program.Dir, Foreground = B(0x6B7383), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 14) });
            CheckBox Opt(string text, bool on) { var c = new CheckBox { IsChecked = on, Foreground = B(0xE6E9EF), FontSize = 13, Margin = new Thickness(0, 0, 0, 9), Content = text }; root.Children.Add(c); return c; }
            var desktop = Opt(L("Verknüpfung auf dem Desktop", "Desktop shortcut"), true);
            var hacker = Opt(L("Hacker-Look sofort aktivieren (startet danach bei jedem Systemstart)", "Activate the Hacker look now (then starts with every boot)"), true);
            var launch = Opt(L("WinMorph nach der Installation öffnen", "Open WinMorph after installing"), true);
            var status = new TextBlock { Foreground = B(0x9AA3B2), FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            var bar = new ProgressBar { Height = 4, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), Foreground = B(0x7C8CFF), Background = B(0x1E222B), BorderThickness = new Thickness(0) };
            var btn = new Button { Content = L("Installieren", "Install"), Height = 44, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 16, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetValue(Border.BackgroundProperty, new LinearGradientBrush(Color.FromRgb(0x7C, 0x8C, 0xFF), Color.FromRgb(0x9B, 0x4D, 0xFF), 0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            btn.Template = tpl;
            root.Children.Add(btn);
            root.Children.Add(bar);
            root.Children.Add(status);
            Content = root;
            bool done = false;
            btn.Click += async (s, e) =>
            {
                if (done) { if (launch.IsChecked == true) Program.Launch(); Close(); return; }
                btn.IsEnabled = false; desktop.IsEnabled = hacker.IsEnabled = false; bar.Visibility = Visibility.Visible;
                bool d = desktop.IsChecked == true, h = hacker.IsChecked == true;
                try
                {
                    await Task.Run(() => Program.Install(d, h, t => Dispatcher.BeginInvoke(new Action(() => status.Text = t))));
                    status.Text = L("Fertig! WinMorph ist installiert – im Startmenü" + (d ? " und auf dem Desktop" : "") + ". Deinstallieren: Einstellungen → Apps.", "Done! WinMorph is installed – in the Start menu" + (d ? " and on the desktop" : "") + ". Uninstall: Settings → Apps.");
                    btn.Content = L("Fertig", "Finish"); done = true;
                }
                catch (Exception ex) { status.Text = L("Fehler: ", "Error: ") + ex.Message; status.Foreground = B(0xFF6B6B); }
                bar.Visibility = Visibility.Collapsed; btn.IsEnabled = true;
            };
        }
    }
}
