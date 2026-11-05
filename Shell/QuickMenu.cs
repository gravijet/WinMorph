using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>The Win+X power menu in the current look.</summary>
    public sealed class QuickMenu : Window
    {
        readonly ShellTheme t;
        bool done;
        static bool De => Core.Loc.De;
        static string L(string de, string en) => De ? de : en;

        public QuickMenu(ShellTheme theme, Rect anchor, Edge edge)
        {
            t = theme;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily(t.Font);
            Deactivated += (s, e) => Done();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Done(); };

            bool retro = t.Btn == BtnStyle.Bevel;
            Color bg = retro ? C(0xC0C0C0) : Core.Applier.Mix(Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B), Colors.Black, 0.05);
            Color fg = retro ? C(0x000000) : (t.Menu == MenuKind.Xp || t.Menu == MenuKind.Seven && t.Id != "mint" ? C(0x1E1E1E) : t.MenuFg);
            if (t.Menu == MenuKind.Xp || (t.Menu == MenuKind.Seven && t.Id != "mint")) bg = C(0xFFFFFF);
            Color hover = retro ? C(0x000080) : (t.Menu == MenuKind.Xp ? C(0x316AC5) : A(t.Accent, 90));

            var sp = new StackPanel { Margin = new Thickness(4) };
            void Item(string text, Action a)
            {
                var tb = new TextBlock { Text = text, FontSize = 12.5, Foreground = B(fg), Margin = new Thickness(10, 0, 30, 0), VerticalAlignment = VerticalAlignment.Center };
                var b = Click(tb, () => { Done(); a(); }, hover, radius: new CornerRadius(retro ? 0 : 4), pad: new Thickness(0, 6, 0, 6));
                b.MouseEnter += (s, e) => tb.Foreground = B(retro || t.Menu == MenuKind.Xp ? C(0xFFFFFF) : fg);
                b.MouseLeave += (s, e) => tb.Foreground = B(fg);
                sp.Children.Add(b);
            }
            void Sep() => sp.Children.Add(new Border { Height = 1, Background = B(A(fg, 50)), Margin = new Thickness(6, 4, 6, 4) });

            Item(L("Installierte Apps", "Installed apps"), () => ShellActions.Run("ms-settings:appsfeatures"));
            Item(L("Energieoptionen", "Power options"), () => ShellActions.Run("ms-settings:powersleep"));
            Item(L("Ereignisanzeige", "Event Viewer"), () => ShellActions.Run("eventvwr.msc"));
            Item("System", () => ShellActions.Run("ms-settings:about"));
            Item(L("Geräte-Manager", "Device Manager"), () => ShellActions.Run("devmgmt.msc"));
            Item(L("Netzwerkverbindungen", "Network connections"), () => ShellActions.Run("ms-settings:network"));
            Item(L("Datenträgerverwaltung", "Disk Management"), () => ShellActions.Run("diskmgmt.msc"));
            Item(L("Computerverwaltung", "Computer Management"), () => ShellActions.Run("compmgmt.msc"));
            Sep();
            Item("Terminal", () => ShellActions.Run("wt.exe"));
            Item(L("Terminal (Administrator)", "Terminal (Admin)"), () => { try { Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true, Verb = "runas" }); } catch { } });
            Item(L("Task-Manager", "Task Manager"), ShellActions.TaskManager);
            Item(L("Einstellungen", "Settings"), ShellActions.Settings);
            Item(L("Explorer", "File Explorer"), () => ShellActions.Files());
            Item(L("Ausführen", "Run"), ShellActions.RunDialog);
            Item("ChatGPT", () => ShellActions.AskAi(null));
            Sep();
            Item(L("Sperren", "Lock"), ShellActions.Lock);
            Item(L("Energie sparen", "Sleep"), ShellActions.Sleep);
            Item(L("Abmelden", "Sign out"), ShellActions.SignOut);
            Item(L("Neu starten", "Restart"), ShellActions.Restart);
            Item(L("Herunterfahren", "Shut down"), ShellActions.Shutdown);
            Item("Desktop", ShellActions.ShowDesktop);
            Sep();
            Item(L("WinMorph (Looks wechseln)", "WinMorph (change look)"), ShellActions.WinMorphApp);
            Item(L("Original-Windows wiederherstellen", "Restore original Windows"), ShellActions.Restore);

            UIElement content;
            if (retro) content = new Bevel { Child = sp };
            else content = new Border
            {
                Child = sp, Background = B(bg), CornerRadius = new CornerRadius(Math.Min(10, t.MenuRadius + 4)), BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1),
                Margin = new Thickness(10), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.4 },
            };
            Content = content;

            Loaded += (s, e) =>
            {
                var mi = W32.Monitor(W32.MonitorAtCursor(), out double sc);
                double wl = mi.rcWork.Left / sc, wt = mi.rcWork.Top / sc, wr = mi.rcWork.Right / sc, wb = mi.rcWork.Bottom / sc;
                double x = anchor.Width > 0 ? anchor.Left : wl, y = edge == Edge.Top ? (anchor.Height > 0 ? anchor.Bottom : wt) : (anchor.Height > 0 ? anchor.Top - ActualHeight : wb - ActualHeight);
                Left = Math.Max(wl, Math.Min(x, wr - ActualWidth));
                Top = Math.Max(wt, Math.Min(y, wb - ActualHeight));
                Activate();
            };
        }

        void Done() { if (done) return; done = true; try { Close(); } catch { } }
    }
}
