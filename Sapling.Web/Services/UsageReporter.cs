using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Sapling.Web.Services;

/// <summary>Counts open Blazor circuits, which is roughly the number of people with the web app open right now.</summary>
public sealed class CircuitCounter : CircuitHandler
{
    private static int _open;

    public static int Open => Volatile.Read(ref _open);

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Increment(ref _open);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Decrement(ref _open);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Posts a system usage report to a Discord webhook (Monitoring:DiscordWebhookUrl) every
/// Monitoring:IntervalMinutes (default 5). Off when no webhook is set. System-wide figures come from /proc,
/// so on Windows only the app's own numbers are reported.
/// </summary>
public sealed class UsageReporter(
    IConfiguration config,
    IHttpClientFactory http,
    IHostEnvironment env,
    ILogger<UsageReporter> log) : BackgroundService
{
    public const string HttpClientName = "discord";

    private readonly DateTime _startedUtc = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = config["Monitoring:DiscordWebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var minutes = Math.Clamp(config.GetValue("Monitoring:IntervalMinutes", 5), 1, 1440);
        log.LogInformation("Monitoring: posting usage to Discord every {Minutes} min.", minutes);

        var cpu = new CpuSampler();
        cpu.Sample();
        await PostAsync(url, Report(cpu, minutes, starting: true), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PostAsync(url, Report(cpu, minutes, starting: false), stoppingToken);
        }
    }

    private object Report(CpuSampler cpu, int minutes, bool starting)
    {
        var (systemCpu, appCpu) = cpu.Sample();
        var mem = MemInfo.Read();
        var process = Process.GetCurrentProcess();
        var gc = GC.GetGCMemoryInfo();
        var disk = Disk();

        var fields = new List<object>
        {
            Field("CPU", systemCpu is { } s ? $"System {s:0}%\nApp {appCpu:0}%" : $"App {appCpu:0}%"),
            Field("App memory", $"{Mb(process.WorkingSet64)} in use\n{Mb(GC.GetTotalMemory(false))} managed heap"),
            Field("Users online", CircuitCounter.Open.ToString(CultureInfo.InvariantCulture)),
        };

        if (mem is { } m)
        {
            fields.Add(Field("System memory", $"{Mb(m.Total - m.Available)} of {Mb(m.Total)} used\n{Mb(m.Available)} free"));
            fields.Add(Field("Swap", m.SwapTotal == 0 ? "none" : $"{Mb(m.SwapTotal - m.SwapFree)} of {Mb(m.SwapTotal)} used"));
        }

        if (Load() is { } load)
        {
            fields.Add(Field("Load (1/5/15 min)", load));
        }

        if (disk is { } d)
        {
            fields.Add(Field("Disk", $"{Gb(d.Free)} free of {Gb(d.Total)}"));
        }

        fields.Add(Field("Uptime", Duration(DateTime.UtcNow - _startedUtc)));
        fields.Add(Field("GC", $"{gc.Index} collections\n{process.Threads.Count} threads"));

        // Amber when memory is tight, red when swap is heavily used: the 1 GB VM's warning signs.
        var usedPct = mem is { } mm && mm.Total > 0 ? 100.0 * (mm.Total - mm.Available) / mm.Total : 0;
        var swapPct = mem is { SwapTotal: > 0 } sm ? 100.0 * (sm.SwapTotal - sm.SwapFree) / sm.SwapTotal : 0;
        var color = swapPct > 50 || usedPct > 92 ? 0xC0392B : usedPct > 80 ? 0xE39A3B : 0x2E8757;

        return new
        {
            username = "Sapling monitor",
            embeds = new[]
            {
                new
                {
                    title = starting ? $"Sapling started on {Environment.MachineName}" : $"Sapling usage · {Environment.MachineName}",
                    description = starting ? $"Environment: {env.EnvironmentName}. Reports every {minutes} min." : null,
                    color,
                    fields,
                    timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                },
            },
        };
    }

    private async Task PostAsync(string url, object payload, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(url, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Monitoring: Discord returned {Status}.", (int)response.StatusCode);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Monitoring: couldn't reach Discord ({Message}).", e.Message);
        }
    }

    private static object Field(string name, string value) => new { name, value, inline = true };

    private static string Mb(long bytes) => $"{bytes / 1024d / 1024d:0} MB";

    private static string Gb(long bytes) => $"{bytes / 1024d / 1024d / 1024d:0.0} GB";

    private static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    private static string? Load()
    {
        try
        {
            var parts = File.ReadAllText("/proc/loadavg").Split(' ');
            return $"{parts[0]} / {parts[1]} / {parts[2]}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (long Free, long Total)? Disk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record MemInfo(long Total, long Available, long SwapTotal, long SwapFree)
    {
        public static MemInfo? Read()
        {
            if (OperatingSystem.IsWindows())
            {
                return Windows.Memory();
            }

            try
            {
                var values = File.ReadLines("/proc/meminfo")
                    .Select(l => l.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => long.Parse(p[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024);
                return new MemInfo(values["MemTotal"], values["MemAvailable"], values.GetValueOrDefault("SwapTotal"), values.GetValueOrDefault("SwapFree"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyNotFoundException or FormatException)
            {
                return null;
            }
        }
    }

    /// <summary>The same system figures on Windows, from kernel32. The page file stands in for swap.</summary>
    private static class Windows
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        public static MemInfo? Memory()
        {
            var status = new MemoryStatusEx { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
            {
                return null;
            }

            // "Page file" in this API is commit limit (RAM + page file), so subtract RAM to get the page file alone.
            var pageTotal = (long)Math.Max(0, (long)status.TotalPageFile - (long)status.TotalPhys);
            var pageFree = (long)Math.Clamp((long)status.AvailPageFile - (long)status.AvailPhys, 0, pageTotal);
            return new MemInfo((long)status.TotalPhys, (long)status.AvailPhys, pageTotal, pageFree);
        }

        /// <summary>Kernel time already includes idle time, so kernel + user is the total.</summary>
        public static (long Idle, long Total) CpuTimes() =>
            GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel + user) : (0, 0);
    }

    /// <summary>CPU % since the previous sample: system-wide from /proc/stat, and this process from its CPU time.</summary>
    private sealed class CpuSampler
    {
        private long _idle;
        private long _total;
        private TimeSpan _appCpu;
        private DateTime _at;

        public (double? System, double App) Sample()
        {
            var now = DateTime.UtcNow;
            var appCpu = Process.GetCurrentProcess().TotalProcessorTime;
            var elapsed = (now - _at).TotalMilliseconds;
            var app = _at == default || elapsed <= 0
                ? 0
                : 100.0 * (appCpu - _appCpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount);
            _appCpu = appCpu;
            _at = now;

            double? system = null;
            if (OperatingSystem.IsWindows())
            {
                if (Windows.CpuTimes() is var (winIdle, winTotal) && winTotal > 0)
                {
                    if (_total > 0 && winTotal > _total)
                    {
                        system = 100.0 * (1 - (double)(winIdle - _idle) / (winTotal - _total));
                    }

                    (_idle, _total) = (winIdle, winTotal);
                }

                return (system, Math.Clamp(app, 0, 100));
            }

            try
            {
                var numbers = File.ReadLines("/proc/stat").First()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(v => long.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                var idle = numbers[3] + (numbers.Length > 4 ? numbers[4] : 0);
                var total = numbers.Sum();
                if (_total > 0 && total > _total)
                {
                    system = 100.0 * (1 - (double)(idle - _idle) / (total - _total));
                }

                (_idle, _total) = (idle, total);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
            {
            }

            return (system, Math.Clamp(app, 0, 100));
        }
    }
}
