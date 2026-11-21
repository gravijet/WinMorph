using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The Zen start menu: a frosted pane that surfaces above the bar like a ring of water. Search at the top,
    /// the pinned apps as pebbles, all apps from A to Z, and at the foot the account and the power stones.
    /// The keyboard moves a koi-orange mark; Enter starts.
    /// </summary>
    public sealed partial class StartMenu
    {
        FrameworkElement zenPane;

        UIElement BuildZen()
        {
            const double W = 620;
            const int Cols = 6;
            var ink = ZenStyle.Ink; var ink2 = ZenStyle.Ink2;
            var root = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };

            // search
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var lens = Glyph("", 17, ZenStyle.Pond);
            lens.Margin = new Thickness(4, 0, 12, 0);
            DockPanel.SetDock(lens, Dock.Left);
            head.Children.Add(lens);
            Search(t.SearchHint, ink, Brushes.Transparent, 18, new CornerRadius(0), new Thickness(0));
            searchBox.FontFamily = ZenStyle.Family;
            head.Children.Add(searchHost);
            var field = new Border { Child = head, CornerRadius = new CornerRadius(22), Background = B(0xFFFFFF, 175), Padding = new Thickness(14, 9, 16, 10), BorderBrush = B(A(ZenStyle.Pond, 0)), BorderThickness = new Thickness(1.5) };
            searchBox.GotKeyboardFocus += (s, e) => field.BorderBrush = B(A(ZenStyle.Pond, 150));
            searchBox.LostKeyboardFocus += (s, e) => field.BorderBrush = B(A(ZenStyle.Pond, 0));
            var headWrap = new Border { Child = field, Margin = new Thickness(0, 0, 0, 16) };
            root.Children.Add(headWrap);

            // section line: title on the left, a quiet switch on the right
            var section = new DockPanel { Margin = new Thickness(4, 0, 4, 10) };
            var toggle = ZenStyle.Text("", 12.5, ZenStyle.Pond, FontWeights.SemiBold);
            toggle.Cursor = Cursors.Hand;
            DockPanel.SetDock(toggle, Dock.Right);
            section.Children.Add(toggle);
            var title = ZenStyle.Text("", 13, ink2, FontWeights.SemiBold);
            section.Children.Add(title);
            root.Children.Add(section);

            var host = new ContentControl { Height = 330 };
            root.Children.Add(host);

            // foot: who is signed in, and the power stones
            var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var stones = new StackPanel { Orientation = Orientation.Horizontal };
            void Stone(string g, string tip, Action a, Color? fg = null) { var st = ZenStyle.Stone(g, () => Do(a), tip, 36, C(0xFFFFFF, 140), fg); st.Margin = new Thickness(6, 0, 0, 0); stones.Children.Add(st); }
            Stone("", L("Einstellungen", "Settings"), ShellActions.Settings);
            Stone("", L("Sperren", "Lock"), ShellActions.Lock);
            Stone("", L("Energie sparen", "Sleep"), ShellActions.Sleep);
            Stone("", L("Neu starten", "Restart"), ShellActions.Restart);
            Stone("", L("Herunterfahren", "Shut down"), ShellActions.Shutdown, ZenStyle.Momiji);
            DockPanel.SetDock(stones, Dock.Right);
            foot.Children.Add(stones);
            var me = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pic = AccountPicture.Get(64);
            var avatar = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderBrush = B(0xFFFFFF, 220), BorderThickness = new Thickness(1.5) };
            if (pic != null) avatar.Background = new ImageBrush(pic) { Stretch = Stretch.UniformToFill };
            else { avatar.Background = B(ZenStyle.Pond); avatar.Child = ZenStyle.Text(ShellActions.UserName.Substring(0, 1).ToUpper(), 15, Colors.White, FontWeights.SemiBold); ((TextBlock)avatar.Child).HorizontalAlignment = HorizontalAlignment.Center; }
            me.Children.Add(avatar);
            var name = ZenStyle.Text(ShellActions.UserName, 13.5, ink, FontWeights.SemiBold);
            name.Margin = new Thickness(10, 0, 0, 0);
            me.Children.Add(name);
            var meBtn = new Border { Child = me, CornerRadius = new CornerRadius(20), Padding = new Thickness(4, 3, 12, 3), Cursor = Cursors.Hand, ToolTip = L("Persönlicher Ordner", "Personal folder") };
            ZenStyle.Press(meBtn, C(0xFFFFFF, 0), C(0xFFFFFF, 140));
            meBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do(() => ShellActions.Folder(Environment.SpecialFolder.UserProfile)); };
            foot.Children.Add(meBtn);
            root.Children.Add(new Border { Height = 1, Background = B(A(ink, 26)), Margin = new Thickness(0, 4, 0, 0) });
            root.Children.Add(foot);

            // ---- the three views: pebbles, A–Z, results ----
            var targets = new List<(Border el, Action run)>();
            int sel = -1;
            void Mark(int i)
            {
                if (sel >= 0 && sel < targets.Count) Highlight(targets[sel].el, false);
                sel = i;
                if (sel >= 0 && sel < targets.Count) { Highlight(targets[sel].el, true); targets[sel].el.BringIntoView(); }
            }

            Border Pebble(ImageSource icon, string text, Action run)
            {
                var st = new StackPanel { Width = 88 };
                var plate = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = B(0xFFFFFF, 165), BorderBrush = B(0xFFFFFF, 230), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Child = icon != null ? (UIElement)Img(icon, 32) : Glyph("", 22, ZenStyle.Pond) };
                st.Children.Add(plate);
                var tb = ZenStyle.Text(text, 12, ink);
                tb.TextAlignment = TextAlignment.Center; tb.Margin = new Thickness(2, 6, 2, 0); tb.HorizontalAlignment = HorizontalAlignment.Center; tb.MaxWidth = 84;
                st.Children.Add(tb);
                var b = new Border { Child = st, CornerRadius = new CornerRadius(18), Padding = new Thickness(4, 8, 4, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = "pebble", ToolTip = text };
                b.MouseEnter += (s, e) => Mark(targets.FindIndex(x => x.el == b));
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do(run); };
                targets.Add((b, run));
                return b;
            }

            Border Row(ImageSource icon, string glyph, string text, string sub, Action run)
            {
                var dock = new DockPanel();
                var mark = new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(2), Background = B(ZenStyle.Koi), Margin = new Thickness(0, 0, 10, 0), Opacity = 0 };
                DockPanel.SetDock(mark, Dock.Left);
                dock.Children.Add(mark);
                FrameworkElement ic = icon != null ? (FrameworkElement)Img(icon, 26) : Glyph(glyph ?? "", 17, ZenStyle.Pond);
                ic.Width = 26; ic.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(ic, Dock.Left);
                dock.Children.Add(ic);
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(ZenStyle.Text(text, 14, ink));
                if (sub != null) words.Children.Add(ZenStyle.Text(sub, 11.5, ink2));
                dock.Children.Add(words);
                var b = new Border { Child = dock, CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 6, 10, 6), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = mark };
                b.MouseEnter += (s, e) => Mark(targets.FindIndex(x => x.el == b));
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do(run); };
                targets.Add((b, run));
                return b;
            }

            ScrollViewer Scroll(UIElement content) => new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Padding = new Thickness(0, 0, 4, 0) };

            int pebbles = 0;
            FrameworkElement azHead = null;
            ScrollViewer scroller = null;
            Action showHome = () =>
            {
                targets.Clear(); sel = -1;
                title.Text = L("Angeheftet", "Pinned");
                toggle.Text = L("Alle Apps", "All apps") + "  ›";
                var all = new StackPanel();
                var g = new UniformGrid { Columns = Cols, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var a in AppCatalog.Pinned()) { var x = a; g.Children.Add(Pebble(x.Icon, x.Name, () => x.Start())); }
                pebbles = targets.Count;
                all.Children.Add(g);
                // all apps from A to Z right below the pebbles
                var az = ZenStyle.Text(L("Alle Apps", "All apps"), 13, ink2, FontWeights.SemiBold);
                az.Margin = new Thickness(4, 18, 0, 4);
                azHead = az;
                all.Children.Add(az);
                char letter = ' ';
                foreach (var a in AppCatalog.Apps)
                {
                    var x = a;
                    char first = char.ToUpperInvariant(x.Name.FirstOrDefault(char.IsLetterOrDigit));
                    if (!char.IsLetter(first)) first = '#';
                    if (first != letter)
                    {
                        letter = first;
                        var lt = ZenStyle.Text(first.ToString(), 13, ZenStyle.Pond, FontWeights.Bold);
                        lt.Margin = new Thickness(10, 10, 0, 2);
                        all.Children.Add(lt);
                    }
                    all.Children.Add(Row(x.Icon, null, x.Name, null, () => x.Start()));
                }
                host.Content = scroller = Scroll(all);
                Fade(host);
            };
            Action<string> showResults = q =>
            {
                targets.Clear(); sel = -1; pebbles = 0;
                title.Text = L("Ergebnisse", "Results");
                toggle.Text = "";
                var list = new StackPanel();
                foreach (var it in Intents.For(q, 6)) { var x = it; list.Children.Add(Row(x.Icon, x.Glyph, x.Title, x.Sub, x.Run)); }
                if (targets.Count == 0) { var none = ZenStyle.Text(L("Nichts gefunden", "Nothing found"), 13.5, ink2); none.Margin = new Thickness(10, 6, 0, 0); list.Children.Add(none); }
                host.Content = Scroll(list);
                Mark(0);
            };
            toggle.MouseLeftButtonUp += (s, e) =>
            {
                if (searchBox.Text.Trim().Length > 0 || azHead == null || scroller == null) return;
                var y = azHead.TransformToAncestor((Visual)scroller.Content).Transform(new Point(0, 0)).Y;
                scroller.ScrollToVerticalOffset(y - 4);
            };
            searchBox.TextChanged += (s, e) =>
            {
                var q = searchBox.Text;
                if (q.Trim().Length == 0) showHome();
                else showResults(q);
            };
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (targets.Count == 0) return;
                int step = 0;
                bool grid = sel < pebbles;
                if (e.Key == Key.Down) step = grid && sel >= 0 && sel + Cols < pebbles ? Cols : grid && sel >= 0 ? pebbles - sel : 1;
                else if (e.Key == Key.Up) step = grid ? -Cols : (sel == pebbles && pebbles > 0 ? -1 : -1);
                else if (e.Key == Key.Right && grid && searchBox.Text.Length == 0) step = 1;
                else if (e.Key == Key.Left && grid && searchBox.Text.Length == 0) step = -1;
                else if (e.Key == Key.Enter) { e.Handled = true; var i = Math.Max(0, sel); Do(targets[i].run); return; }
                if (step == 0) return;
                e.Handled = true;
                int next = sel < 0 ? 0 : Math.Max(0, Math.Min(targets.Count - 1, sel + step));
                Mark(next);
            };
            showHome();
            resetView = () => { showHome(); scroller?.ScrollToTop(); };
            WhenAppsReady(() => { if (searchBox.Text.Length == 0) showHome(); });

            var pane = ZenStyle.Pane(root, 28, 165);
            pane.Width = W;
            zenPane = pane;
            // room for the soft shadow: the window is larger than the pane
            return new Border { Child = pane, Padding = new Thickness(30, 22, 30, 34), Background = Brushes.Transparent };
        }

        static void Highlight(Border b, bool on)
        {
            var to = on ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(0, 255, 255, 255);
            var brush = b.Background as SolidColorBrush;
            if (brush == null || brush.IsFrozen) { brush = new SolidColorBrush(((SolidColorBrush)b.Background)?.Color ?? Colors.Transparent); b.Background = brush; }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(on ? 120 : 200)));
            if (b.Tag is Border mark) mark.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 140 : 200)));
        }

        static void Fade(FrameworkElement el) => el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)));

        /// <summary>The pane surfaces from the lotus button like a ring of water.</summary>
        void SurfaceZen()
        {
            if (zenPane == null) return;
            double ox = 0.5;
            if (anchor.Width > 0 && zenPane.ActualWidth > 0)
            {
                var p = zenPane.PointToScreen(new Point(0, 0));
                W32.PrimaryMonitor(out double sc);
                ox = Math.Max(0.05, Math.Min(0.95, (anchor.Left + anchor.Width / 2 - p.X / sc) / zenPane.ActualWidth));
            }
            ZenStyle.Ripple(zenPane, new Point(ox, 1.0), 460);
        }
    }
}
