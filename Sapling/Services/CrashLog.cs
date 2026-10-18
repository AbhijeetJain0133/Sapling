namespace Sapling.Services;

/// <summary>
/// Saves the last fatal exception to app storage so the next launch can show it; release builds have
/// no debugger attached, and this is the only way to see why a tester's phone crashed.
/// </summary>
public static class CrashLog
{
    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "last-crash.txt");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Save(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Save(e.Exception);
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Save(e.Exception);
#endif
    }

    public static void Save(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}");
        }
        catch
        {
            // Nothing more can be done while the process is going down.
        }
    }

    /// <summary>Returns and clears the saved crash, so it is shown once.</summary>
    public static string? Take()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var text = File.ReadAllText(FilePath);
            File.Delete(FilePath);
            return text;
        }
        catch
        {
            return null;
        }
    }
}
