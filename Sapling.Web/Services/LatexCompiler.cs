using System.Diagnostics;
using System.Text;

namespace Sapling.Web.Services;

public sealed record LatexCompileResult(bool Ok, byte[]? Pdf, string Log);

/// <summary>Turns a LaTeX document into a PDF on the server.</summary>
public interface ILatexCompiler
{
    /// <summary>False when the compiler binary could not be found; the editor then says so instead of failing.</summary>
    bool IsAvailable { get; }

    Task<LatexCompileResult> CompileAsync(string latex, CancellationToken ct);
}

/// <summary>
/// Runs Tectonic (https://tectonic-typesetting.github.io), a self-contained XeTeX that downloads the packages a
/// document needs on first use and caches them. Each compile gets its own temp folder, shell escape is off
/// (--untrusted), and at most two compiles run at once.
/// </summary>
public sealed class TectonicCompiler : ILatexCompiler
{
    public const int MaxLatexChars = 60_000;
    private const int MaxLogChars = 6_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    // The first compile on a machine downloads the packages and fonts the templates use (tens of MB).
    private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromMinutes(5);

    private readonly string _executable;
    private readonly string _cacheDir;
    private readonly ILogger<TectonicCompiler> _log;
    private readonly SemaphoreSlim _gate = new(2);
    private readonly Lazy<bool> _available;
    private readonly Lazy<Task> _warmUp;

    public TectonicCompiler(IConfiguration config, ILogger<TectonicCompiler> log)
    {
        _log = log;
        _executable = string.IsNullOrWhiteSpace(config["Latex:TectonicPath"]) ? "tectonic" : config["Latex:TectonicPath"]!;

        // Tectonic caches packages in the user's local app data by default. An IIS app pool account usually has no
        // loaded profile, so Tectonic fails with "Unable to find standard directories". An explicit folder avoids that.
        _cacheDir = !string.IsNullOrWhiteSpace(config["Latex:CacheDir"])
            ? config["Latex:CacheDir"]!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sapling", "tectonic-cache");
        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.LogWarning("LaTeX: can't create the package cache folder {Dir} ({Message}). Set Latex:CacheDir to a folder the app can write to.", _cacheDir, e.Message);
        }
        _available = new Lazy<bool>(Probe);
        _warmUp = new Lazy<Task>(() => Task.Run(WarmUpAsync));
    }

    /// <summary>
    /// Compiles both templates once at startup so Tectonic downloads and caches every package and font they use.
    /// Students' compiles wait for this instead of racing it for the same downloads.
    /// </summary>
    public Task WarmUpTask => _warmUp.Value;

    private async Task WarmUpAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        var sample = new Sapling.Shared.Contracts.ResumeDataDto(
            new("Sample Name", "sample@example.com", "+91 90000 00000", "Indore", "linkedin.com/in/sample", "github.com/sample", ""),
            "Sample summary.",
            [new("Sample College", "B.Tech", "Computer Science", "2022", "2026", "CGPA 8.0/10", ["Coursework"])],
            [new("Sample Org", "Intern", "Remote", "May 2025", "Jul 2025", ["Built a thing."])],
            [new("Sample Project", "github.com/sample/project", "C#, SQL", "", "", ["Did a thing."])],
            [new("Languages", ["Java", "Python"])],
            [new("Sample Award", "NPTEL", "2024", "Top 5%")]);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var template in new[] { Sapling.Shared.Contracts.ResumeTemplates.SingleColumn, Sapling.Shared.Contracts.ResumeTemplates.TwoColumn })
        {
            var result = await CompileCoreAsync(LatexTemplates.Render(template, sample), WarmUpTimeout, CancellationToken.None);
            if (!result.Ok)
            {
                _log.LogWarning("LaTeX: warm-up compile of the {Template} template failed; the first student compile will be slow.\n{Log}", template, result.Log);
                return;
            }
        }

        _log.LogInformation("LaTeX: package cache ready in {Seconds:0.0} s.", watch.Elapsed.TotalSeconds);
    }

    public bool IsAvailable => _available.Value;

    private bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(_executable, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            var version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            _log.LogInformation("LaTeX: using {Version} ({Path}).", version, _executable);
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _log.LogWarning("LaTeX: Tectonic was not found at '{Path}'. Install it or set Latex:TectonicPath; resume PDF preview is off.", _executable);
            return false;
        }
    }

    public async Task<LatexCompileResult> CompileAsync(string latex, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return new(false, null, "Tectonic is not installed on this server, so the PDF can't be built. Install Tectonic or set Latex:TectonicPath.");
        }

        if (string.IsNullOrWhiteSpace(latex) || latex.Length > MaxLatexChars)
        {
            return new(false, null, $"The document is empty or longer than {MaxLatexChars:N0} characters.");
        }

        // While the startup warm-up is still downloading packages, wait for it; afterwards compiles take seconds.
        var warm = _warmUp.Value;
        var timeout = Timeout;
        if (!warm.IsCompleted)
        {
            await warm.WaitAsync(ct);
        }

        return await CompileCoreAsync(latex, timeout, ct);
    }

    private async Task<LatexCompileResult> CompileCoreAsync(string latex, TimeSpan limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var dir = Path.Combine(Path.GetTempPath(), "sapling-tex", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "main.tex"), latex, new UTF8Encoding(false), ct);

            var start = new ProcessStartInfo(_executable)
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment["TECTONIC_CACHE_DIR"] = _cacheDir;

            // --reruns 0: the second TeX pass only fixes PDF bookmarks, which a resume doesn't need; it saves ~2 s.
            foreach (var arg in new[] { "-X", "compile", "--untrusted", "--keep-logs", "--reruns", "0", "--outdir", dir, "main.tex" })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = start };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => Append(output, e.Data);
            process.ErrorDataReceived += (_, e) => Append(output, e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limit);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                ct.ThrowIfCancellationRequested();
                return new(false, null, $"Compiling took longer than {limit.TotalSeconds:0} seconds and was stopped. Try again; if it keeps happening, check the server's internet connection.");
            }

            var pdfPath = Path.Combine(dir, "main.pdf");
            var ok = process.ExitCode == 0 && File.Exists(pdfPath);
            var log = Tail(Clean(output.ToString(), dir));
            return new(ok, ok ? await File.ReadAllBytesAsync(pdfPath, ct) : null, log);
        }
        finally
        {
            _gate.Release();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Append(StringBuilder sb, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (sb)
        {
            sb.AppendLine(line);
        }
    }

    // Server paths mean nothing to the student.
    private static string Clean(string log, string dir) => log.Replace(dir + Path.DirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase).Replace(dir, "", StringComparison.OrdinalIgnoreCase);

    private static string Tail(string log) => log.Length <= MaxLogChars ? log.Trim() : "…" + log[^MaxLogChars..].Trim();
}
