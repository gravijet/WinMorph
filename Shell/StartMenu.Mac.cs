using System;
using System.Collections.Generic;
using System.Linq;
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
    /// macOS: Launchpad. The desktop blurs and darkens, the apps appear seven by five per page with their names,
    /// a search field on top, page dots below. Pages slide with the wheel or the arrow keys; opening zooms the
    /// icons in from slightly larger, closing zooms them out again.
    /// </summary>
    public sealed partial class StartMenu
    {
        Grid lpLayer, lpPages, lpBackdrop;
        StackPanel lpStrip, lpDots;
        TranslateTransform lpSlide;
        int lpPage, lpPageCount;
        double lpW, lpH;
        DateTime lpWheel;
        readonly Dictionary<int, Grid> lpBuilt = new Dictionary<int, Grid>();
        List<AppEntry> lpApps = new List<AppEntry>();
        const int LpCols = 7, LpRows = 5, LpPer = LpCols * LpRows;

        UIElement BuildLaunchpad()
        {
            fullScreen = true;
            var root = new Grid { Background = B(0x000000, 1), ClipToBounds = true };
            lpBackdrop = new Grid { IsHitTestVisible = false };
            var bg = new Image { Source = MacStyle.FrostImage, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(bg, BitmapScalingMode.HighQuality);
            lpBackdrop.Children.Add(bg);
            lpBackdrop.Children.Add(new Border { Background = B(0x000000, 64) });
            root.Children.Add(lpBackdrop);

            lpLayer = new Grid();
            root.Children.Add(lpLayer);

            // the search field
            var field = new Grid { Width = 236, Height = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 46, 0, 0) };
            var box = Search(L("Suche", "Search"), Colors.White, B(0xFFFFFF, 46), 13.5, new CornerRadius(9), new Thickness(30, 0, 10, 0), C(0xFFFFFF, 60));
            box.FontFamily = MacStyle.Font;
            searchHost.Height = 30;
            field.Children.Add(searchHost);
            var glass = MacStyle.Sym("magnifyingglass", 13, C(0xFFFFFF, 190));
            glass.HorizontalAlignment = HorizontalAlignment.Left; glass.Margin = new Thickness(10, 0, 0, 0); glass.IsHitTestVisible = false;
            field.Children.Add(glass);
            lpLayer.Children.Add(field);

            lpPages = new Grid { ClipToBounds = false, Margin = new Thickness(0, 104, 0, 64) };
            lpStrip = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            lpSlide = new TranslateTransform();
            lpStrip.RenderTransform = lpSlide;
            lpPages.Children.Add(lpStrip);
            lpLayer.Children.Add(lpPages);

            lpDots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 30) };
            lpLayer.Children.Add(lpDots);

            // a click on the empty background closes, as on the Mac
            root.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == root || e.OriginalSource == lpPages || e.OriginalSource is Grid g && g.Tag as string == "page") SafeClose(); };
            root.MouseWheel += (s, e) => { if ((DateTime.Now - lpWheel).TotalMilliseconds < 350) return; lpWheel = DateTime.Now; GoPage(lpPage + (e.Delta < 0 ? 1 : -1)); };
            root.SizeChanged += (s, e) => { if (Math.Abs(e.NewSize.Width - lpW) > 1 || Math.Abs(e.NewSize.Height - lpH) > 1) { lpW = e.NewSize.Width; lpH = e.NewSize.Height; Layout(); } };
            PreviewKeyDown += (s, e) =>
            {
                if (!IsFullScreen || t.Menu != MenuKind.Launchpad) return;
                if (searchBox.Text.Length == 0 && e.Key == Key.Right) { GoPage(lpPage + 1); e.Handled = true; }
                else if (searchBox.Text.Length == 0 && e.Key == Key.Left) { GoPage(lpPage - 1); e.Handled = true; }
                else if (e.Key == Key.Enter && lpApps.Count > 0 && searchBox.Text.Trim().Length > 0) { Launch(lpApps[0]); e.Handled = true; }
            };
            searchBox.TextChanged += (s, e) => { lpPage = 0; Layout(); };
            resetView = () => { lpPage = 0; lpSlide.BeginAnimation(TranslateTransform.XProperty, null); lpSlide.X = 0; };
            WhenAppsReady(Layout);
            return root;
        }

        void Layout()
        {
            if (lpStrip == null || lpW < 10) return;
            var f = searchBox?.Text.Trim() ?? "";
            lpApps = f.Length > 0 ? Apps(f).Take(LpPer).ToList() : AppCatalog.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            lpPageCount = Math.Max(1, (lpApps.Count + LpPer - 1) / LpPer);
            lpPage = Math.Min(lpPage, lpPageCount - 1);
            lpStrip.Children.Clear(); lpBuilt.Clear();
            for (int i = 0; i < lpPageCount; i++) lpStrip.Children.Add(new Grid { Width = lpW, Tag = "page", Background = Brushes.Transparent });
            BuildPage(lpPage);
            lpSlide.BeginAnimation(TranslateTransform.XProperty, null);
            lpSlide.X = -lpPage * lpW;
            Dots();
        }

        void BuildPage(int p)
        {
            if (p < 0 || p >= lpPageCount || lpBuilt.ContainsKey(p)) return;
            var host = (Grid)lpStrip.Children[p];
            double avail = Math.Min(lpW * 0.82, 1500), gridH = Math.Max(200, lpH - 104 - 64);
            double cellW = avail / LpCols, cellH = gridH / LpRows;
            double icon = Math.Max(48, Math.Min(Math.Min(cellW * 0.6, cellH * 0.64), 104));
            var grid = new Grid { Width = avail, Height = gridH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Tag = "page", Background = Brushes.Transparent };
            for (int c = 0; c < LpCols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < LpRows; r++) grid.RowDefinitions.Add(new RowDefinition());
            int k = 0;
            foreach (var a in lpApps.Skip(p * LpPer).Take(LpPer))
            {
                var app = a;
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, Cursor = Cursors.Arrow };
                var img = new Image { Source = MacStyle.Tile(app.Icon), Width = icon, Height = icon };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                sp.Children.Add(img);
                var name = MacStyle.Text(app.Name, 12.5, Colors.White);
                name.MaxWidth = cellW - 14; name.HorizontalAlignment = HorizontalAlignment.Center; name.TextAlignment = TextAlignment.Center; name.Margin = new Thickness(0, 4, 0, 0);
                sp.Children.Add(name);
                var dim = new ScaleTransform(1, 1);
                img.RenderTransformOrigin = new Point(0.5, 0.5); img.RenderTransform = dim;
                sp.MouseLeftButtonDown += (s, e) => { img.Opacity = 0.6; e.Handled = true; };
                sp.MouseLeave += (s, e) => img.Opacity = 1;
                sp.MouseLeftButtonUp += (s, e) => { e.Handled = true; if (img.Opacity < 1) { img.Opacity = 1; Launch(app); } };
                sp.MouseRightButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    var items = new List<MacMenu.Item>();
                    if (AppCatalog.IsPinned(app)) items.Add(new MacMenu.Item { Text = L("Aus dem Dock entfernen", "Remove from Dock"), Run = () => AppCatalog.Unpin(app) });
                    else items.Add(new MacMenu.Item { Text = L("Im Dock behalten", "Keep in Dock"), Run = () => AppCatalog.Pin(app) });
                    items.Add(new MacMenu.Item { Text = L("Öffnen", "Open"), Run = () => Launch(app) });
                    MacMenu.AtCursor(items);
                };
                Grid.SetColumn(sp, k % LpCols); Grid.SetRow(sp, k / LpCols);
                grid.Children.Add(sp);
                k++;
            }
            grid.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.35 };
            if (lpApps.Count == 0 && (searchBox?.Text.Trim().Length ?? 0) > 0)
            {
                var none = MacStyle.Text(L("Keine Ergebnisse", "No Results"), 17, C(0xFFFFFF, 200));
                none.HorizontalAlignment = HorizontalAlignment.Center; none.VerticalAlignment = VerticalAlignment.Top; none.Margin = new Thickness(0, 120, 0, 0);
                host.Children.Add(none);
            }
            host.Children.Add(grid);
            lpBuilt[p] = grid;
        }

        void GoPage(int p)
        {
            if (lpStrip == null || p < 0 || p >= lpPageCount || p == lpPage) return;
            BuildPage(p);
            lpPage = p;
            lpSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-p * lpW, TimeSpan.FromMilliseconds(380)) { EasingFunction = MacStyle.OutQuint });
            Dots();
        }

        void Dots()
        {
            lpDots.Children.Clear();
            if (lpPageCount < 2) return;
            for (int i = 0; i < lpPageCount; i++)
            {
                int k = i;
                var d = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(5, 0, 5, 0), Fill = B(0xFFFFFF, (byte)(i == lpPage ? 235 : 90)), Cursor = Cursors.Arrow };
                d.MouseLeftButtonUp += (s, e) => { e.Handled = true; GoPage(k); };
                lpDots.Children.Add(d);
            }
        }

        /// <summary>Opening: the backdrop fades in, the apps zoom down from slightly larger.</summary>
        void SurfaceLaunchpad()
        {
            lpBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            lpLayer.RenderTransformOrigin = new Point(0.5, 0.5);
            var sc = new ScaleTransform(1.1, 1.1);
            lpLayer.RenderTransform = sc;
            var a = new DoubleAnimation(1.1, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = MacStyle.OutQuint };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            lpLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        /// <summary>Closing: the apps zoom up and fade with the backdrop, then the window hides.</summary>
        bool CloseLaunchpad()
        {
            if (t.Menu != MenuKind.Launchpad || !IsVisible || lpLayer == null) return false;
            var sc = lpLayer.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            lpLayer.RenderTransform = sc;
            var a = new DoubleAnimation(1.08, TimeSpan.FromMilliseconds(170)) { EasingFunction = MacStyle.In };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, a); sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            lpLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190));
            fade.Completed += (s, e) =>
            {
                if (!closing) return;   // opened again meanwhile
                try { Hide(); } catch { }
                lpLayer.BeginAnimation(OpacityProperty, null); lpLayer.Opacity = 1;
                lpBackdrop.BeginAnimation(OpacityProperty, null);
                try { resetView?.Invoke(); if (searchBox != null) searchBox.Text = ""; } catch { }
            };
            lpBackdrop.BeginAnimation(OpacityProperty, fade);
            return true;
        }
    }
}
