using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>Themed Alt+Tab window switcher with live previews.</summary>
    public sealed class Switcher : Window
    {
        readonly ShellTheme t;
        readonly List<WinInfo> wins;
        readonly List<Border> cells = new List<Border>();
        readonly List<FrameworkElement> thumbTargets = new List<FrameworkElement>();
        readonly List<LiveThumb> thumbs = new List<LiveThumb>();
        TextBlock caption;
        int index;
        FrameworkElement textPreview;
        LiveThumb textThumb;
        static Color Solid(Color c) => Color.FromRgb(c.R, c.G, c.B);

        public WinInfo Selected => wins.Count == 0 ? null : wins[index];

        public Switcher(ShellTheme theme, List<WinInfo> windows, int start)
        {
            t = theme; wins = windows; index = Math.Max(0, Math.Min(start, wins.Count - 1));
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true;
            FontFamily = new FontFamily(t.Font);
            Content = Build();
            RenderGuard.Watch(this);
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                SetWindowLong(h, W32.GWL_EXSTYLE, ex | W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE);
                int corner = t.Switch == SwitchKind.Classic || t.Switch == SwitchKind.Text || (t.Frame?.Square ?? false) ? 1 : 2;
                W32.DwmSetWindowAttribute(h, W32.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            };
            Loaded += (s, e) =>
            {
                Center();
                foreach (var (w, target) in Pairs()) { var lt = new LiveThumb(this, w.Hwnd, target); if (lt.Ok) thumbs.Add(lt); }
                Select(index);
            };
            Closed += (s, e) => { foreach (var th in thumbs) th.Dispose(); thumbs.Clear(); textThumb?.Dispose(); };
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        IEnumerable<(WinInfo, FrameworkElement)> Pairs()
        {
            for (int i = 0; i < thumbTargets.Count && i < wins.Count; i++) yield return (wins[i], thumbTargets[i]);
        }

        void Center()
        {
            var mi = W32.Monitor(W32.MonitorAtCursor(), out double s);
            double mw = (mi.rcWork.Right - mi.rcWork.Left) / s, mh = (mi.rcWork.Bottom - mi.rcWork.Top) / s;
            Left = mi.rcWork.Left / s + (mw - ActualWidth) / 2;
            Top = mi.rcWork.Top / s + (mh - ActualHeight) / 2;
        }

        public void Move(int d)
        {
            if (wins.Count == 0) return;
            Select((index + d + wins.Count) % wins.Count);
        }

        void Select(int i)
        {
            index = i;
            for (int k = 0; k < cells.Count; k++) Style(cells[k], k == i);
            if (caption != null && wins.Count > 0) caption.Text = wins[i].Title;
            if (textPreview != null && IsLoaded && i < wins.Count)
            {
                textThumb?.Dispose();
                textThumb = new LiveThumb(this, wins[i].Hwnd, textPreview);
            }
        }

        void Style(Border b, bool on)
        {
            switch (t.Switch)
            {
                case SwitchKind.Classic:
                    b.BorderBrush = on ? B(t.Id == "xp" ? C(0x316AC5) : t.Id == "mint" ? t.Accent : C(0x000080)) : Brushes.Transparent;
                    b.Background = on && t.Id == "xp" ? B(0xC1D2EE) : Brushes.Transparent;
                    break;
                case SwitchKind.Mac:
                    b.Background = on ? B(t.Id == "zen" ? A(t.BarFg, 40) : C(0xFFFFFF, 46)) : Brushes.Transparent;
                    break;
                case SwitchKind.Text:
                    b.Background = on ? B(t.Accent) : Brushes.Transparent;
                    if (b.Child is TextBlock tb) tb.Foreground = B(on ? C(0x000000) : t.MenuFg);
                    break;
                default:
                    b.BorderBrush = on ? B(t.Accent) : B(A(t.MenuFg, 25));
                    b.Background = on ? B(A(t.Accent, 60)) : B(A(t.MenuFg, 10));
                    break;
            }
        }

        UIElement Build()
        {
            bool de = Core.Loc.De;
            Color bg = Solid(t.MenuBg);
            if (t.Switch == SwitchKind.Mac) bg = t.Id == "zen" ? C(0xF7F2E9) : C(0x262628);
            Background = B(bg);

            if (wins.Count == 0)
                return new TextBlock { Text = de ? "Keine offenen Fenster" : "No open windows", Foreground = B(t.MenuFg), FontSize = 15, Margin = new Thickness(40, 24, 40, 24) };

            switch (t.Switch)
            {
                case SwitchKind.Text:
                {
                    var mono = new FontFamily("Consolas");
                    var dim = t.BarFgDim;
                    var sp = new StackPanel { Margin = new Thickness(10, 8, 10, 10), Width = 640 };
                    sp.Children.Add(new TextBlock { Text = "[ alt+tab ]  " + wins.Count + (Core.Loc.De ? " fenster" : " windows") + "   ·   tab / ← → wählen · esc abbrechen", Foreground = B(dim), FontFamily = mono, FontSize = 12.5, Margin = new Thickness(6, 0, 6, 8) });
                    sp.Children.Add(new TextBlock { Text = "  #   PID     PROCESS          TITLE", Foreground = B(dim), FontFamily = mono, FontSize = 13, Margin = new Thickness(6, 0, 6, 2) });
                    for (int i = 0; i < wins.Count && i < 18; i++)
                    {
                        W32.GetWindowThreadProcessId(wins[i].Hwnd, out uint pid);
                        var name = (wins[i].ExePath != null ? System.IO.Path.GetFileNameWithoutExtension(wins[i].ExePath) : "?").ToLowerInvariant();
                        if (name.Length > 15) name = name.Substring(0, 15);
                        var tb = new TextBlock { Text = " " + (i + 1).ToString().PadLeft(2) + "   " + pid.ToString().PadRight(7) + " " + name.PadRight(16) + " " + wins[i].Title, FontFamily = mono, FontSize = 14, Foreground = B(t.MenuFg), TextTrimming = TextTrimming.CharacterEllipsis };
                        var b = new Border { Child = tb, Padding = new Thickness(6, 3, 6, 3) };
                        cells.Add(b); sp.Children.Add(b);
                    }
                    var preview = new Grid { Width = 420, Height = 262, Margin = new Thickness(0, 34, 12, 12), VerticalAlignment = VerticalAlignment.Top };
                    preview.Children.Add(new TextBlock { Text = Core.Loc.De ? "keine vorschau" : "no preview", Foreground = B(dim), FontFamily = mono, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    textPreview = preview;
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(sp);
                    row.Children.Add(new Border { Child = preview, BorderBrush = B(A(t.Accent, 120)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 34, 12, 12), VerticalAlignment = VerticalAlignment.Top });
                    preview.Margin = new Thickness(0);
                    return new Border { Child = row, BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1) };
                }
                case SwitchKind.Classic:
                {
                    var grid = new WrapPanel { MaxWidth = 7 * 50, HorizontalAlignment = HorizontalAlignment.Center };
                    foreach (var w in wins)
                    {
                        var b = new Border { Width = 46, Height = 46, Margin = new Thickness(2), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(t.Id == "xp" ? 4 : 0), Child = Img(w.Icon, 32) };
                        cells.Add(b); grid.Children.Add(b);
                    }
                    Color fg = t.Id == "mint" ? t.MenuFg : C(0x000000);
                    caption = new TextBlock { Foreground = B(fg), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 330, Margin = new Thickness(6, 8, 6, 2) };
                    var sp = new StackPanel { Margin = new Thickness(12) };
                    sp.Children.Add(grid);
                    if (t.Id == "win95") { var cap = new Bevel { Sunken = true, Child = caption, Margin = new Thickness(0, 8, 0, 0) }; sp.Children.Add(cap); caption.Margin = new Thickness(6, 3, 6, 3); }
                    else sp.Children.Add(caption);
                    if (t.Id == "win95") { Background = B(0xC0C0C0); return new Bevel { Child = sp }; }
                    if (t.Id == "xp") { Background = B(0xFFFFFF); return new Border { Child = sp, BorderBrush = B(0x0831D9), BorderThickness = new Thickness(2), Background = V((0, C(0xFFFFFF)), (1, C(0xE3ECFA))) }; }
                    return new Border { Child = sp, BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(1) };
                }
                case SwitchKind.Mac:
                {
                    var row = new WrapPanel { MaxWidth = 8 * 116, HorizontalAlignment = HorizontalAlignment.Center };
                    foreach (var w in wins)
                    {
                        var b = new Border { Width = 108, Height = 108, Margin = new Thickness(4), CornerRadius = new CornerRadius(20), Child = Img(w.Icon, 80) };
                        cells.Add(b); row.Children.Add(b);
                    }
                    Color fg = t.Id == "zen" ? t.BarFg : C(0xF2F2F2);
                    caption = new TextBlock { Foreground = B(fg), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 700, Margin = new Thickness(0, 10, 0, 0) };
                    var sp = new StackPanel { Margin = new Thickness(18, 18, 18, 14) };
                    sp.Children.Add(row); sp.Children.Add(caption);
                    return sp;
                }
                default:
                {
                    var wrap = new WrapPanel { MaxWidth = 5 * 252, HorizontalAlignment = HorizontalAlignment.Center };
                    foreach (var w in wins)
                    {
                        var thumbArea = new Grid { Width = 224, Height = 130 };
                        thumbArea.Children.Add(Img(w.Icon, 48)); // shown when there is no live picture (e.g. minimised)
                        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 2, 0) };
                        title.Children.Add(Img(w.Icon, 16));
                        title.Children.Add(new TextBlock { Text = w.Title, Foreground = B(t.MenuFg), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), MaxWidth = 196, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                        var sp = new StackPanel();
                        sp.Children.Add(thumbArea); sp.Children.Add(title);
                        var b = new Border { Child = sp, Padding = new Thickness(8), Margin = new Thickness(6), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(t.Frame?.Square ?? false ? 0 : 10) };
                        cells.Add(b); thumbTargets.Add(thumbArea); wrap.Children.Add(b);
                    }
                    var outer = new Border { Child = wrap, Padding = new Thickness(14), BorderBrush = B(C(0x808080, 70)), BorderThickness = new Thickness(t.Layout == BarLayout.Floating || t.Layout == BarLayout.AiOnly || t.Layout == BarLayout.Waybar ? 1 : 0) };
                    return outer;
                }
            }
        }
    }
}
