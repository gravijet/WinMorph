using System;
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
    public static partial class LockScreen
    {
        /// <summary>
        /// macOS: the date and the large time on top, input language, Wi-Fi and battery at the upper right, the
        /// account picture, name and password field at the bottom, "Switch User" below. A wrong password shakes
        /// the field; Windows Hello is offered in the line under the field when it is set up.
        /// </summary>
        static void MacLock(Grid root, FrameworkElement bg, LockAuth auth)
        {
            root.Children.Add(bg);
            root.Children.Add(new Border { Background = V((0, C(0x000000, 84)), (0.36, C(0x000000, 10)), (0.62, C(0x000000, 0)), (1, C(0x000000, 84))) });
            var white = C(0xFFFFFF);
            var soft = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.42 };

            var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 16, 0), Effect = soft };
            string lang = "DE";
            try { lang = InputLanguageManager.Current.CurrentInputLanguage.TwoLetterISOLanguageName.ToUpperInvariant(); } catch { }
            var langText = MacStyle.Text(lang, 13, white, FontWeights.SemiBold);
            langText.Margin = new Thickness(0, 0, 14, 0);
            status.Children.Add(langText);
            var wifiHost = new Border { Width = 18, Margin = new Thickness(0, 0, 14, 0) };
            status.Children.Add(wifiHost);
            var batHost = new Border { Width = 27 };
            status.Children.Add(batHost);
            Tick(() =>
            {
                if (DateTime.Now.Second % 5 != 0 && wifiHost.Child != null) return;
                var cur = Wlan.Current;
                wifiHost.Child = MacStyle.Sym(cur != null || System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable() ? "wifi" : "wifi.slash", 17, white, cur != null ? Math.Min(2, cur.Signal / 34) : 2);
                var b = Hardware.Battery();
                batHost.Child = b != null ? MacStyle.Sym("battery", 27, white, b.Percent) : null;
                batHost.Visibility = b != null ? Visibility.Visible : Visibility.Collapsed;
            });
            root.Children.Add(status);

            var top = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 58, 0, 0), Effect = soft };
            var date = MacStyle.Text("", 22, C(0xFFFFFF, 235), FontWeights.SemiBold);
            date.HorizontalAlignment = HorizontalAlignment.Center;
            var time = MacStyle.Text("", 124, C(0xFFFFFF, 230), FontWeights.SemiBold, display: true);
            time.HorizontalAlignment = HorizontalAlignment.Center; time.Margin = new Thickness(0, -14, 0, 0);
            top.Children.Add(date); top.Children.Add(time);
            // the clock grows with the screen, as large as Tahoe draws it
            root.SizeChanged += (o, e) =>
            {
                double h = e.NewSize.Height;
                if (h < 100) return;
                time.FontSize = Math.Max(96, Math.Min(220, h * 0.158));
                date.FontSize = Math.Max(19, Math.Min(30, h * 0.026));
                time.Margin = new Thickness(0, -time.FontSize * 0.11, 0, 0);
                top.Margin = new Thickness(0, Math.Max(40, h * 0.06), 0, 0);
            };
            Tick(() => { date.Text = DateTime.Now.ToString(De ? "dddd, d. MMMM" : "dddd, MMMM d", Ci); time.Text = DateTime.Now.ToString(De ? "H:mm" : "h:mm"); });
            root.Children.Add(top);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 96) };
            var pic = AccountPicture.Get(192);
            var avatar = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Effect = soft, Background = Brushes.Transparent };
            if (pic != null) avatar.Children.Add(new Ellipse { Fill = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } });
            else
            {
                avatar.Children.Add(new Ellipse { Fill = new LinearGradientBrush(C(0xA9A9AE), C(0x6E6E73), 90) });
                var initials = string.Concat(ShellActions.UserName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpper(x[0])));
                var it = MacStyle.Text(initials, 26, white, FontWeights.Medium);
                it.HorizontalAlignment = HorizontalAlignment.Center;
                avatar.Children.Add(it);
            }
            mid.Children.Add(avatar);
            var name = MacStyle.Text(ShellActions.UserName, 15, white, FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 10, 0, 12); name.Effect = soft;
            mid.Children.Add(name);

            var hold = MacStyle.Text(L("Passwort eingeben", "Enter Password"), 13, C(0xFFFFFF, 175));
            hold.Margin = new Thickness(17, 0, 0, 0);
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var caret = new Rectangle { Width = 1.6, Height = 16, Fill = B(white), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(1060) };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(530))));
            caret.BeginAnimation(UIElement.OpacityProperty, blink);
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 34, 0), ClipToBounds = true };
            line.Children.Add(dots); line.Children.Add(caret);
            var go = new Grid { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0), Visibility = Visibility.Hidden, Cursor = Cursors.Arrow, Background = Brushes.Transparent };
            go.Children.Add(new Ellipse { Fill = B(0xFFFFFF, 80) });
            var arrow = MacStyle.Sym("arrow.right", 12, white);
            go.Children.Add(arrow);
            var spinner = new Ellipse { Width = 16, Height = 16, Stroke = B(0xFFFFFF), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3.5, 2.5 }, Visibility = Visibility.Collapsed, RenderTransformOrigin = new Point(0.5, 0.5) };
            go.Children.Add(spinner);
            go.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth?.Submit?.Invoke(); };
            var fieldGrid = new Grid();
            fieldGrid.Children.Add(hold); fieldGrid.Children.Add(line); fieldGrid.Children.Add(go);
            var field = new Border { Width = 200, Height = 30, CornerRadius = new CornerRadius(15), Background = B(0xFFFFFF, 64), BorderBrush = B(0xFFFFFF, 90), BorderThickness = new Thickness(0.7), Child = fieldGrid, HorizontalAlignment = HorizontalAlignment.Center };
            mid.Children.Add(field);
            var note = MacStyle.Text("", 12, C(0xFFFFFF, 215));
            note.HorizontalAlignment = HorizontalAlignment.Center; note.Margin = new Thickness(0, 10, 0, 0); note.MinHeight = 16; note.Effect = soft;
            mid.Children.Add(note);
            root.Children.Add(mid);

            var switchUser = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24), Cursor = Cursors.Arrow, Background = Brushes.Transparent, Effect = soft };
            var su = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
            su.Children.Add(new Ellipse { Fill = B(0xFFFFFF, 56) });
            su.Children.Add(MacStyle.Sym("person", 15, white));
            switchUser.Children.Add(su);
            var suText = MacStyle.Text(L("Benutzer wechseln", "Switch User"), 11.5, C(0xFFFFFF, 225));
            suText.HorizontalAlignment = HorizontalAlignment.Center; suText.Margin = new Thickness(0, 5, 0, 0);
            switchUser.Children.Add(suText);
            switchUser.MouseLeftButtonUp += (o, e) => { e.Handled = true; auth?.UseWindows?.Invoke(); };
            root.Children.Add(switchUser);
            if (auth == null) return;
            var drop = new TranslateTransform(0, -14);
            top.RenderTransform = drop;
            drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(620)) { EasingFunction = MacStyle.OutQuint });
            top.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
            foreach (var el in new UIElement[] { mid, switchUser, status }) el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(120) });

            auth.Terminal = false; auth.Accent = MacStyle.Blue; auth.AlwaysOpen = true;
            avatar.MouseLeftButtonUp += (o, e) => { if (auth.HelloReady) auth.UseHello?.Invoke(); };
            var spin = new RotateTransform();
            spinner.RenderTransform = spin;
            bool busyShown = false;
            auth.Render = () =>
            {
                if (auth.Unlocked) return;
                if (auth.Busy != busyShown)
                {
                    busyShown = auth.Busy;
                    arrow.Visibility = busyShown ? Visibility.Collapsed : Visibility.Visible;
                    spinner.Visibility = busyShown ? Visibility.Visible : Visibility.Collapsed;
                    if (busyShown) spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
                    else spin.BeginAnimation(RotateTransform.AngleProperty, null);
                }
                int want = Math.Min(auth.Length, 16);
                while (dots.Children.Count > want) dots.Children.RemoveAt(dots.Children.Count - 1);
                while (dots.Children.Count < want) dots.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = B(white), Margin = new Thickness(0, 0, 5, 0) });
                caret.Visibility = auth.Busy ? Visibility.Hidden : Visibility.Visible;
                hold.Visibility = auth.Length == 0 ? Visibility.Visible : Visibility.Hidden;
                go.Visibility = auth.Length > 0 || auth.Busy ? Visibility.Visible : Visibility.Hidden;
                note.Text = auth.Note ?? (auth.HelloReady ? L("Windows Hello oder Passwort eingeben", "Windows Hello or enter password") : "");
            };
            auth.OnDenied = () => { auth.Render(); MacStyle.Shake(field, 11); };
            auth.OnGranted = () =>
            {
                foreach (var el in new UIElement[] { mid, top, status, switchUser }) el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
            };
            Tick(auth.Tick);
        }
    }
}
