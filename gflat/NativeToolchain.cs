using System.Diagnostics;

namespace gflat;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class NativeToolchain
{
    private static readonly Lazy<string> DefaultClang = new(DiscoverClang);
    private static readonly Lazy<string?> LibraryDirectory = new(() =>
    {
        string? lib = VisualStudioInstallations()
            .Select(installation => Path.Combine(installation, "VC", "Tools", "MSVC"))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Select(version => Path.Combine(version, "lib", "x64", "libcmt.lib"))
            .Where(File.Exists)
            .OrderDescending().FirstOrDefault();
        return lib == null ? null : Path.GetDirectoryName(lib);
    });

    private static IEnumerable<string> VisualStudioInstallations()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        // Only visit version/edition directories, not the entire installation tree.
        // A recursive search can take minutes on a fresh CI runner, outside Run's timeout.
        var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft Visual Studio"))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
            foreach (string version in Directory.EnumerateDirectories(root))
                foreach (string edition in Directory.EnumerateDirectories(version))
                    yield return edition;
    }
    public static string FindClang(string? requested = null)
    {
        requested ??= Environment.GetEnvironmentVariable("GFLAT_CLANG");
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        return DefaultClang.Value;
    }

    private static string DiscoverClang()
    {
        string name = OperatingSystem.IsWindows() ? "clang.exe" : "clang";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        foreach (string installation in VisualStudioInstallations())
        {
            string candidate = Path.Combine(installation, "VC", "Tools", "Llvm", "x64", "bin", "clang.exe");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Clang was not found. Install LLVM or set GFLAT_CLANG to its executable path.");
    }

    public static ProcessResult Run(string executable, IEnumerable<string> arguments, int timeoutMilliseconds = 30000)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"'{executable}' exceeded {timeoutMilliseconds} ms.");
        }
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    public static ProcessResult Compile(string irPath, string outputPath, string? clang = null)
    {
        string optimization = Environment.GetEnvironmentVariable("GFLAT_OPT_LEVEL") ?? "0";
        if (optimization is not ("0" or "1" or "2" or "3"))
            throw new ArgumentException("GFLAT_OPT_LEVEL must be 0, 1, 2, or 3.");
        var args = new List<string> { irPath, "-O" + optimization, "-o", outputPath };
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIB")))
        {
            if (LibraryDirectory.Value is string lib) args.AddRange(["-Xlinker", "/libpath:" + lib]);
        }
        return Run(FindClang(clang), args, 60000);
    }
}
