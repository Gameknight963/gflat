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
        if (!string.IsNullOrWhiteSpace(requested)) return FindOnPath(requested, includeCurrentDirectory: true) ?? requested;
        return DefaultClang.Value;
    }

    private static string? FindOnPath(string name, bool includeCurrentDirectory = false)
    {
        // Resolve a supplied executable name without changing explicit paths or
        // falling back to a different compiler when that name cannot be found.
        if (name != Path.GetFileName(name)) return null;
        string filename = OperatingSystem.IsWindows() && Path.GetExtension(name).Length == 0 ? name + ".exe" : name;
        if (includeCurrentDirectory && File.Exists(filename)) return Path.GetFullPath(filename);
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(dir.Trim('"'), filename);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static string DiscoverClang()
    {
        string name = OperatingSystem.IsWindows() ? "clang.exe" : "clang";
        if (FindOnPath(name) is string found) return found;
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

    /// <summary>Run user code with inherited input/output so console prompts remain interactive.</summary>
    public static int RunProgram(string executable, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }

    public static ProcessResult CompileCpp(string source, string output, IEnumerable<string>? objects = null, int exceptionReserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes, int optimizationLevel = 2)
    {
        gflat.Projects.OptimizationLevel.Parse(optimizationLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string compiler = FindClang(null);
        var args = new List<string> { "--driver-mode=g++", "-std=c++17", "-O" + optimizationLevel, "-fexceptions", source, "-o", output, NativeRuntime.GetObject(compiler, exceptionReserveBytes) };
        args.AddRange(NativeRuntime.HostArguments());
        if (objects != null) args.AddRange(objects);
        return Run(compiler, args, 60000);
    }

    public static ProcessResult Compile(string irPath, string outputPath, string? clang = null, int exceptionReserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes, int? optimizationLevel = null)
    {
        string optimization = optimizationLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Environment.GetEnvironmentVariable("GFLAT_OPT_LEVEL") ?? "0";
        gflat.Projects.OptimizationLevel.Parse(optimization);
        var args = new List<string> { irPath, "-O" + optimization, "-o", outputPath };
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIB")))
        {
            if (LibraryDirectory.Value is string lib) args.AddRange(["-Xlinker", "/libpath:" + lib]);
        }
        string compiler = FindClang(clang);
        string ir = File.ReadAllText(irPath);
        // MSVC's runtime lacks helpers such as __divti3/__modti3 used for
        // 128-bit operations. Let this Clang installation select its matching
        // compiler-rt builtins; ordinary-width programs keep the default runtime.
        if (OperatingSystem.IsWindows() && System.Text.RegularExpressions.Regex.IsMatch(ir, @"\bi128\b"))
            args.Add("--rtlib=compiler-rt");
        bool exceptions = ir.Contains("@__gflat_eh_", StringComparison.Ordinal);
        bool stdio = ir.Contains("@__gflat_stdio_", StringComparison.Ordinal);
        if (exceptions || stdio)
        {
            // Keep the selected LLVM version, including names such as clang-18.
            args.Add("--driver-mode=g++");
            if (exceptions) args.Add(NativeRuntime.GetObject(compiler, exceptionReserveBytes));
            if (stdio) args.Add(NativeRuntime.GetStdioObject(compiler));
            args.AddRange(NativeRuntime.HostArguments());
        }
        // LLVM may lower floating remainder to fmod/fmodf. A bare program uses
        // the C driver, which doesn't add libm as the C++ driver does on Linux.
        if (OperatingSystem.IsLinux() && System.Text.RegularExpressions.Regex.IsMatch(ir, @"(?m)^\s*%[^\r\n]*=\s*frem\b"))
            args.Add("-lm");
        return Run(compiler, args, 60000);
    }
}
