using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using AColor = Android.Graphics.Color;

namespace Sapling
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        // The app's own surface colours (--surface in tokens.css), so the system bars read as part of the app.
        private static readonly AColor LightBars = AColor.ParseColor("#FFFFFF");
        private static readonly AColor DarkBars = AColor.ParseColor("#1A1A1A");

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            KeepClearOfSystemBars();
            PaintSystemBars();

            // Picking a theme inside the app changes this without a configuration change.
            if (Microsoft.Maui.Controls.Application.Current is { } app)
            {
                app.RequestedThemeChanged += (_, _) => PaintSystemBars();
            }
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            PaintSystemBars();
        }

        /// <summary>
        /// Recent Android versions draw apps edge to edge, which put the tab bar under the phone's back/home
        /// buttons and the top bar under the status bar. Padding the content by the system insets keeps the
        /// whole app between the two bars on every phone, and above the keyboard when it is open.
        /// </summary>
        private void KeepClearOfSystemBars()
        {
            if (FindViewById(Android.Resource.Id.Content) is not { } content)
            {
                return;
            }

            ViewCompat.SetOnApplyWindowInsetsListener(content, new InsetsListener());
            ViewCompat.RequestApplyInsets(content);
        }

        private void PaintSystemBars()
        {
            if (Window is not { } window)
            {
                return;
            }

            var dark = IsDark();
            var colour = dark ? DarkBars : LightBars;

            // The padded strips behind the bars show the window background, so it carries the colour on
            // edge-to-edge Android; the bar colours themselves still apply on older versions.
            window.DecorView.SetBackgroundColor(colour);
#pragma warning disable CA1422 // Ignored from Android 15, where the window background above does the job.
            window.SetStatusBarColor(colour);
            window.SetNavigationBarColor(colour);
#pragma warning restore CA1422

            // Dark icons on the light theme, light icons on the dark one.
            if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            {
                controller.AppearanceLightStatusBars = !dark;
                controller.AppearanceLightNavigationBars = !dark;
            }
        }

        /// <summary>The theme the student chose in the app, or the phone's own when they left it on "system".</summary>
        private bool IsDark() => Preferences.Default.Get("sapling-theme", "system") switch
        {
            "dark" => true,
            "light" => false,
            _ => (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes,
        };

        private sealed class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
        {
            public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? insets)
            {
                if (view is null || insets is null)
                {
                    return insets;
                }

                var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
                var keyboard = insets.GetInsets(WindowInsetsCompat.Type.Ime());
                if (bars is not null)
                {
                    view.SetPadding(bars.Left, bars.Top, bars.Right, Math.Max(bars.Bottom, keyboard?.Bottom ?? 0));
                }

                return WindowInsetsCompat.Consumed;
            }
        }
    }
}
