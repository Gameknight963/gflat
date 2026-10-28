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

    /// <summary>Capture a process's output within one timeout covering exit and both output streams; -1 waits indefinitely.</summary>
    public static ProcessResult Run(string executable, IEnumerable<string> arguments, int timeoutMilliseconds = 30000)
    {
        if (timeoutMilliseconds < -1) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = new CancellationTokenSource(timeoutMilliseconds);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        Task completion = Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
        try
        {
            completion.WaitAsync(deadline.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Readers may still be finishing cancellation; observe late faults without waiting for EOF.
            _ = completion.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                process.WaitForExit();
            }
            throw new TimeoutException($"'{executable}' exceeded {timeoutMilliseconds} ms.");
        }
        return new(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    /// <summary>Run user code with inherited input/output so console prompts remain interactive.</summary>
    public static int RunProgram(string executable, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false } };
        gflat.Projects.MsBuildHost.RestoreUserProcessEnvironment(process.StartInfo.Environment);
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
        AddUnusedCodeElimination(args);
        args.AddRange(NativeRuntime.HostArguments());
        if (objects != null) args.AddRange(objects);
        return Run(compiler, args, 60000);
    }

    public static ProcessResult Compile(string irPath, string outputPath, string? clang = null, int exceptionReserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes, int? optimizationLevel = null, gflat.Projects.NativeLinkInputs? nativeLibraries = null)
    {
        string optimization = optimizationLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Environment.GetEnvironmentVariable("GFLAT_OPT_LEVEL") ?? "0";
        gflat.Projects.OptimizationLevel.Parse(optimization);
        var linkInputs = (nativeLibraries ?? gflat.Projects.NativeLinkInputs.Empty).Normalize(Environment.CurrentDirectory);
        var paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        string fullOutput = Path.GetFullPath(outputPath);
        string ir = File.ReadAllText(irPath);
        bool debug = ir.Contains("!llvm.dbg.cu", StringComparison.Ordinal);
        if (linkInputs.Libraries.Any(library => library.Kind == "File" &&
            (paths.Equals(library.Value, fullOutput) ||
             (debug && OperatingSystem.IsWindows() && paths.Equals(library.Value, Path.ChangeExtension(fullOutput, ".pdb"))))))
            throw new ArgumentException("Output must differ from native library files.");
        var args = new List<string> { irPath, "-O" + optimization, "-o", outputPath };
        AddUnusedCodeElimination(args);
        if (OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIB")))
        {
            if (LibraryDirectory.Value is string lib) args.AddRange(["-Xlinker", "/libpath:" + lib]);
        }
        string compiler = FindClang(clang);
        if (debug)
        {
            args.Add("-g");
            if (OperatingSystem.IsWindows()) args.AddRange(["-gcodeview", "-Xlinker", "/DEBUG:FULL", "-Xlinker", "/PDB:" + Path.ChangeExtension(Path.GetFullPath(outputPath), ".pdb")]);
        }
        // MSVC lacks the helpers for wide division and float conversions.
        // Wide comparisons, bit operations, and unused interface implementations
        // do not by themselves require Clang's compiler-rt library.
        if (OperatingSystem.IsWindows() && RequiresWideIntegerRuntime(ir))
            args.Add("--rtlib=compiler-rt");
        var (exceptions, stdio) = RequiredNativeRuntimes(ir);
        if (exceptions || stdio)
        {
            // Keep the selected LLVM version, including names such as clang-18.
            args.Add("--driver-mode=g++");
            if (exceptions) args.Add(NativeRuntime.GetObject(compiler, exceptionReserveBytes));
            if (stdio) args.Add(NativeRuntime.GetStdioObject(compiler));
            args.AddRange(NativeRuntime.HostArguments());
        }
        AddNativeLibraries(args, linkInputs);
        // LLVM may lower floating remainder to fmod/fmodf. A bare program uses
        // the C driver, which doesn't add libm as the C++ driver does on Linux.
        if (OperatingSystem.IsLinux() && System.Text.RegularExpressions.Regex.IsMatch(ir, @"(?m)^\s*%[^\r\n]*=\s*frem\b"))
            args.Add("-lm");
        return Run(compiler, args, 60000);
    }

    internal static void AddNativeLibraries(List<string> arguments, gflat.Projects.NativeLinkInputs inputs)
    {
        foreach (string directory in inputs.Directories) arguments.Add("-L" + directory);
        // Static archives are searched in order. Preserve repeats and interleaved
        // named/file inputs rather than treating these as a set of dependencies.
        foreach (var library in inputs.Libraries)
            arguments.Add(library.Kind == "Name" ? "-l" + library.Value : library.Value);
    }

    internal static (bool Exceptions, bool Stdio) RequiredNativeRuntimes(string ir)
    {
        if (!ir.Contains("__gflat_", StringComparison.Ordinal)) return default;
        // Keep line boundaries and quoted global identifiers, but hide metadata,
        // string constants, inline assembly text and comments from header matching.
        char[] code = ir.ToCharArray();
        for (int at = 0; at < ir.Length; at++)
        {
            if (ir[at] == ';')
            {
                while (at < ir.Length && ir[at] != '\n') code[at++] = ' ';
            }
            else if (ir[at] == '"')
            {
                bool globalSymbol = at > 0 && ir[at - 1] == '@';
                if (!globalSymbol) code[at] = ' ';
                for (at++; at < ir.Length; at++)
                {
                    if (!globalSymbol && ir[at] is not ('\r' or '\n')) code[at] = ' ';
                    if (ir[at] == '\\' && at + 1 < ir.Length)
                    {
                        at++;
                        if (!globalSymbol) code[at] = ' ';
                        continue;
                    }
                    if (ir[at] == '"') break;
                }
            }
        }
        // The first @ in an emitted function header names that function. Do not
        // skip a different name to find a reserved-looking name in its body.
        const string header = "(?m)^[\\t ]*(?:declare|define)\\b[^@{}=]*@(?:\"__gflat_(?<runtime>eh|stdio)_[A-Za-z0-9_]+\"|__gflat_(?<runtime>eh|stdio)_[A-Za-z0-9_]+)\\s*\\(";
        bool exceptions = false, stdio = false;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(new string(code), header))
        {
            if (match.Groups["runtime"].Value == "eh") exceptions = true;
            else stdio = true;
            if (exceptions && stdio) break;
        }
        return (exceptions, stdio);
    }

    internal static bool RequiresWideIntegerRuntime(string ir)
    {
        if (!ir.Contains("i128", StringComparison.Ordinal)) return false;
        const string instruction = @"(?m)^\s*%[^=\r\n]+\s*=\s*";
        return System.Text.RegularExpressions.Regex.IsMatch(ir, instruction + @"(?:sdiv|udiv|srem|urem)\s+(?:exact\s+)?i128\b") ||
            System.Text.RegularExpressions.Regex.IsMatch(ir, instruction + @"(?:sitofp|uitofp)\s+i128\b") ||
            System.Text.RegularExpressions.Regex.IsMatch(ir, instruction + @"(?:fptosi|fptoui)\s+[^\r\n;]*\bto\s+i128\b");
    }

    private static void AddUnusedCodeElimination(List<string> arguments)
    {
        arguments.AddRange(["-ffunction-sections", "-fdata-sections"]);
        arguments.AddRange(OperatingSystem.IsWindows()
            ? ["-Xlinker", "/OPT:REF"]
            : ["-Xlinker", "--gc-sections"]);
    }
}
