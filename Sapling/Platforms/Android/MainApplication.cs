using Android.App;
using Android.Runtime;

namespace Sapling
{
#if DEBUG
    // The dev server speaks plain HTTP; Android blocks that unless the app opts in.
    [Application(UsesCleartextTraffic = true)]
#else
    [Application]
#endif
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
            Sapling.Services.CrashLog.Install();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
