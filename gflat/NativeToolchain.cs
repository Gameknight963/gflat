using System.Diagnostics;

namespace gflat;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public static class NativeToolchain
{
    private static readonly Lazy<string> DefaultClang = new(DiscoverClang);
    private static readonly Lazy<string?> LibraryDirectory = new(() =>
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio");
        if (!OperatingSystem.IsWindows() || !Directory.Exists(root)) return null;
        string? lib = Directory.EnumerateFiles(root, "libcmt.lib", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(@"\lib\x64\libcmt.lib", StringComparison.OrdinalIgnoreCase))
            .OrderDescending().FirstOrDefault();
        return lib == null ? null : Path.GetDirectoryName(lib);
    });
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
        if (OperatingSystem.IsWindows())
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio");
            if (Directory.Exists(root))
            {
                string? candidate = Directory.EnumerateFiles(root, "clang.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(p => p.Contains(@"\Llvm\x64\bin\", StringComparison.OrdinalIgnoreCase));
                if (candidate != null) return candidate;
            }
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
