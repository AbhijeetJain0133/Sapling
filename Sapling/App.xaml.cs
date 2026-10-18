namespace Sapling
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // After a crash, show what happened once (so testers can screenshot it), then carry on.
            var crash = Services.CrashLog.Take();
            var window = new Window(crash is null ? new MainPage() : CrashPage(crash)) { Title = "Sapling" };

#if WINDOWS
            // The Windows head exists only for quick testing, so it opens at phone proportions.
            window.Width = 430;
            window.Height = 880;
            window.MinimumWidth = 360;
            window.MaximumWidth = 560;
#endif

            return window;
        }

        private static ContentPage CrashPage(string details)
        {
            var page = new ContentPage { Padding = new Thickness(20, 48, 20, 20), BackgroundColor = Colors.White };
            var carryOn = new Button { Text = "Continue to Sapling", BackgroundColor = Color.FromArgb("#006838"), TextColor = Colors.White };
            carryOn.Clicked += (_, _) => page.Window!.Page = new MainPage();

            var grid = new Grid
            {
                RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)],
                RowSpacing = 12,
            };
            grid.Add(new Label { Text = "Sapling closed unexpectedly last time. Please screenshot this and send it to the team.", TextColor = Colors.Black, FontSize = 15 }, 0, 0);
            grid.Add(new ScrollView { Content = new Label { Text = details, TextColor = Colors.DimGray, FontSize = 11 } }, 0, 1);
            grid.Add(carryOn, 0, 2);

            page.Content = grid;
            return page;
        }
    }
}
