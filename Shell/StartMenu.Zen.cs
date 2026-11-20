using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using static WinMorph.Shell.Ui;

namespace WinMorph.Shell
{
    /// <summary>
    /// The Zen start menu: a hanging scroll (kakejiku). Washi paper between two wooden rods; it unrolls
    /// downwards when opened. Search line with an ensō, a brush stroke below it, favourites as quiet rows,
    /// the chosen one marked with a small vermilion seal.
    /// </summary>
    public sealed partial class StartMenu
    {
        static readonly Color Washi = C(0xFBF8F2), WashiDeep = C(0xF3ECDF), Ink = C(0x3F362A), Umber = C(0x6B5B45), Sand = C(0x9C8E79), Shu = C(0xB5452B);

        // scroll parts the opening animation moves
        RectangleGeometry zenClip;
        TranslateTransform zenRod;
        FrameworkElement zenPaper;

        static Border Rod(double width)
        {
            // a turned wooden rod with darker knobs at both ends
            var g = new Grid { Height = 12, Width = width };
            g.Children.Add(new Border { Margin = new Thickness(10, 1, 10, 1), CornerRadius = new CornerRadius(5), Background = V((0, C(0x8A6B4C)), (0.45, C(0x6B5038)), (1, C(0x4A3624))) });
            g.Children.Add(new Border { Width = 16, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(6), Background = V((0, C(0x5C4430)), (1, C(0x34261A))) });
            g.Children.Add(new Border { Width = 16, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new CornerRadius(6), Background = V((0, C(0x5C4430)), (1, C(0x34261A))) });
            return new Border { Child = g, Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = C(0x2A1E12) } };
        }

        static FrameworkElement Enso(double size)
        {
            // an ink circle, left open at the top right like a brush lifted off the paper
            // drawn as a filled brush stroke: thick where the brush lands, thin where it lifts off
            double r = size * 0.42, c = size / 2;
            Point P(double deg, double rr) { double a = deg * Math.PI / 180; return new Point(c + rr * Math.Cos(a), c + rr * Math.Sin(a)); }
            var fig = new PathFigure { StartPoint = P(-60, r + size * 0.07), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new ArcSegment(P(250, r + size * 0.012), new Size(r + size * 0.04, r + size * 0.04), 0, true, SweepDirection.Clockwise, true));
            fig.Segments.Add(new ArcSegment(P(-60, r - size * 0.07), new Size(r - size * 0.04, r - size * 0.04), 0, true, SweepDirection.Counterclockwise, true));
            return new Path
            {
                Data = new PathGeometry(new[] { fig }), Fill = B(Umber),
                Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        static Path BrushLine(double width) => new Path
        {
            Data = Geometry.Parse("M0,3 C40,0.6 120,0.2 160,1.4 C200,2.4 280,1.2 320,3.4 C280,4.6 200,4.2 160,4.6 C120,5 40,5.4 0,3 Z"),
            Fill = B(Umber), Opacity = 0.55, Width = width, Height = 5, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center,
        };

        UIElement BuildZen()
        {
            const double W = 560;
            var paper = new Border
            {
                Width = W - 28, HorizontalAlignment = HorizontalAlignment.Center,
                Background = V((0, Washi), (1, WashiDeep)), BorderBrush = B(0xE2D6C2), BorderThickness = new Thickness(1, 0, 1, 0),
                Padding = new Thickness(22, 16, 22, 14),
            };
            var sp = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(2, 4, 2, 10) };
            var enso = Enso(30); enso.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(enso, Dock.Left);
            head.Children.Add(enso);
            Search(t.SearchHint, Ink, Brushes.Transparent, 21, new CornerRadius(0), new Thickness(0));
            searchBox.FontFamily = new FontFamily("Segoe UI Light");
            head.Children.Add(searchHost);
            sp.Children.Add(head);
            sp.Children.Add(BrushLine(W - 90));

            var label = new TextBlock { Foreground = B(Sand), FontSize = 11.5, Margin = new Thickness(10, 14, 0, 6), FontFamily = new FontFamily("Segoe UI Semilight") };
            sp.Children.Add(label);
            var results = new StackPanel();
            sp.Children.Add(results);
            int selected = 0;
            var actions = new List<Action>();
            var rows = new List<(Border row, Ellipse seal)>();

            void Mark(int i, bool on)
            {
                if (i < 0 || i >= rows.Count) return;
                var (row, seal) = rows[i];
                row.Background = on ? B(A(C(0xA0805A), 34)) : Brushes.Transparent;
                seal.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(on ? 160 : 90)));
            }

            Action<string> fill = q =>
            {
                results.Children.Clear(); actions.Clear(); rows.Clear(); selected = 0;
                q = q ?? "";
                void Add(ImageSource icon, string glyph, string text, string sub, Action a)
                {
                    var g = new Grid { Height = 44 };
                    var seal = new Ellipse { Width = 7, Height = 7, Fill = B(Shu), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), Opacity = 0 };
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0) };
                    if (icon != null) line.Children.Add(Img(icon, 24));
                    else { var gl = Glyph(glyph ?? "", 17, Umber); gl.Width = 24; line.Children.Add(gl); }
                    var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                    words.Children.Add(new TextBlock { Text = text, FontSize = 14.5, Foreground = B(Ink), FontFamily = new FontFamily("Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis });
                    if (sub != null) words.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = B(Sand) });
                    line.Children.Add(words);
                    g.Children.Add(seal); g.Children.Add(line);
                    var row = new Border { Child = g, CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                    int index = rows.Count;
                    row.MouseEnter += (s, e) => { Mark(selected, false); selected = index; Mark(selected, true); };
                    row.MouseLeftButtonUp += (s, e) => { e.Handled = true; Do(a); };
                    results.Children.Add(row); actions.Add(a); rows.Add((row, seal));
                }
                if (q.Trim().Length == 0)
                {
                    label.Text = L("F A V O R I T E N", "F A V O U R I T E S");
                    foreach (var app in AppCatalog.Pinned()) { var x = app; Add(x.Icon, null, x.Name, null, () => x.Start()); }
                }
                else
                {
                    label.Text = L("G E F U N D E N", "F O U N D");
                    foreach (var it in Intents.For(q, 6)) { var x = it; Add(x.Icon, x.Glyph, x.Title, x.Sub, x.Run); }
                    if (rows.Count == 0) results.Children.Add(new TextBlock { Text = L("Nichts gefunden. Atme aus, versuch ein anderes Wort.", "Nothing found. Breathe out, try another word."), Foreground = B(Sand), FontSize = 13.5, Margin = new Thickness(10, 8, 0, 8) });
                }
                Mark(0, true);
            };
            searchBox.TextChanged += (s, e) => fill(searchBox.Text);
            searchBox.PreviewKeyDown += (s, e) =>
            {
                if (rows.Count == 0) return;
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    Mark(selected, false);
                    selected = (selected + (e.Key == Key.Down ? 1 : rows.Count - 1)) % rows.Count;
                    Mark(selected, true);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter) { e.Handled = true; Do(actions[selected]); }
            };
            fill(null);
            WhenAppsReady(() => fill(searchBox.Text));

            // the foot of the scroll: quiet ways to stop for the day
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            void FootLink(string glyph, string text, Action a)
            {
                var c = new StackPanel { Orientation = Orientation.Horizontal };
                c.Children.Add(Glyph(glyph, 12, Sand));
                c.Children.Add(new TextBlock { Text = text, Foreground = B(Umber), FontSize = 12.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                foot.Children.Add(Click(c, () => Do(a), A(C(0xA0805A), 30), radius: new CornerRadius(8), pad: new Thickness(10, 5, 10, 5)));
            }
            FootLink("", L("Sperren", "Lock"), ShellActions.Lock);
            FootLink("", L("Ruhen", "Sleep"), ShellActions.Sleep);
            FootLink("", L("Neu starten", "Restart"), ShellActions.Restart);
            FootLink("", L("Ausschalten", "Shut down"), ShellActions.Shutdown);
            sp.Children.Add(new Border { Height = 1, Background = B(A(Umber, 40)), Margin = new Thickness(10, 12, 10, 0) });
            sp.Children.Add(foot);
            paper.Child = sp;

            var top = Rod(W);
            var bottom = Rod(W);
            zenRod = new TranslateTransform();
            bottom.RenderTransform = zenRod;
            zenClip = new RectangleGeometry();
            paper.Clip = zenClip;
            paper.SizeChanged += (s, e) => { if (zenClip.Rect.Height == 0 || !IsVisible) zenClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height); };
            zenPaper = paper;

            var scroll = new StackPanel { Width = W };
            scroll.Children.Add(top);
            scroll.Children.Add(paper);
            scroll.Children.Add(bottom);
            // room around the scroll for its shadow: the window is larger than the scroll, so no square corners show
            return new Border
            {
                Child = scroll, Padding = new Thickness(26, 18, 26, 30), Background = Brushes.Transparent,
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Direction = 270, Opacity = 0.22, Color = C(0x3A2A1A) },
            };
        }

        /// <summary>The scroll unrolls from the top rod: the paper is revealed while the bottom rod travels down.</summary>
        void UnrollZen()
        {
            if (zenPaper == null || zenClip == null) return;
            zenPaper.UpdateLayout();
            double w = zenPaper.ActualWidth, h = zenPaper.ActualHeight;
            if (h <= 0) return;
            var dur = TimeSpan.FromMilliseconds(!SystemParameters.ClientAreaAnimation ? 1 : 340);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            zenClip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, w, 0), new Rect(0, 0, w, h), dur) { EasingFunction = ease });
            zenRod.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-h, 0, dur) { EasingFunction = ease });
        }
    }
}
