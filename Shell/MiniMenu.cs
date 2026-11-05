using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>Small right-click menu in the current look, shown at the mouse pointer.</summary>
    public sealed class MiniMenu : Window
    {
        bool done;

        public static void Show(ShellTheme t, IEnumerable<(string text, Action action)> items)
        {
            HoverPreview.CloseNow();
            var m = new MiniMenu(t, items);
            m.Show();
            m.Activate();
        }

        MiniMenu(ShellTheme t, IEnumerable<(string text, Action action)> items)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = new FontFamily(t.Font);
            Deactivated += (s, e) => Done();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Done(); };

            bool retro = t.Btn == BtnStyle.Bevel;
            bool term = t.Btn == BtnStyle.Text;
            bool light = t.Menu == MenuKind.Xp || (t.Menu == MenuKind.Seven && t.Id != "mint");
            Color bg = retro ? C(0xC0C0C0) : light ? C(0xFFFFFF) : Color.FromRgb(t.MenuBg.R, t.MenuBg.G, t.MenuBg.B);
            Color fg = retro || light ? C(0x000000) : t.MenuFg;
            Color hover = retro ? C(0x000080) : light ? C(0x316AC5) : A(t.Accent, 90);
            Color hoverFg = retro || light ? C(0xFFFFFF) : fg;

            var sp = new StackPanel { Margin = new Thickness(4), MinWidth = 180 };
            foreach (var (text, action) in items)
            {
                if (text == "-") { sp.Children.Add(new Border { Height = 1, Background = B(A(fg, 50)), Margin = new Thickness(6, 3, 6, 3) }); continue; }
                var a = action;
                var tb = new TextBlock { Text = text, FontSize = 12.5, Foreground = B(fg), Margin = new Thickness(10, 0, 24, 0) };
                if (term) tb.FontFamily = new FontFamily("Consolas");
                var b = Click(tb, () => { Done(); a(); }, term ? t.Accent : hover, radius: new CornerRadius(retro || term ? 0 : 4), pad: new Thickness(0, 6, 0, 6));
                if (term) { b.MouseEnter += (s, e) => tb.Foreground = B(0x000000); b.MouseLeave += (s, e) => tb.Foreground = B(fg); }
                b.MouseEnter += (s, e) => tb.Foreground = B(hoverFg);
                b.MouseLeave += (s, e) => tb.Foreground = B(fg);
                sp.Children.Add(b);
            }
            Content = retro ? (UIElement)new Bevel { Child = sp } : new Border
            {
                Child = sp, Background = B(term ? C(0x000000) : bg), CornerRadius = new CornerRadius(term ? 0 : 8), BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1),
                Margin = new Thickness(8), Effect = term ? null : new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 },
            };
            Loaded += (s, e) =>
            {
                W32.GetCursorPos(out var p);
                var mi = W32.Monitor(W32.MonitorFromPoint(p, 2), out double sc);
                double x = p.X / sc, y = p.Y / sc;
                double wl = mi.rcWork.Left / sc, wt = mi.rcWork.Top / sc, wr = mi.rcWork.Right / sc, wb = mi.rcWork.Bottom / sc;
                if (y + ActualHeight > wb) y -= ActualHeight;
                Left = Math.Max(wl, Math.Min(x, wr - ActualWidth));
                Top = Math.Max(wt, Math.Min(y, wb - ActualHeight));
            };
        }

        void Done() { if (done) return; done = true; try { Close(); } catch { } }
    }
}
