using System.Security.Cryptography;

namespace gflat;

/// <summary>Build the small C++ exception runtime using the host's native C++ ABI.</summary>
internal static class NativeRuntime
{
    private static readonly object BuildLock = new();
    private static readonly Dictionary<string, string> Objects = new();

    internal static List<string> HostArguments()
    {
        var args = new List<string>();
        if (!OperatingSystem.IsWindows()) return args;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft Visual Studio");
        string? version = Directory.Exists(root) ? Directory.GetDirectories(root)
            .SelectMany(year => Directory.GetDirectories(year))
            .Select(edition => Path.Combine(edition, "VC", "Tools", "MSVC"))
            .Where(Directory.Exists).SelectMany(Directory.GetDirectories)
            .OrderDescending().FirstOrDefault() : null;
        if (version != null)
        {
            args.AddRange(["-isystem", Path.Combine(version, "include"), "-Xlinker", "/libpath:" + Path.Combine(version, "lib", "x64")]);
        }
        string kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
        string includes = Path.Combine(kits, "Include");
        string? sdk = Directory.Exists(includes) ? Directory.GetDirectories(includes).OrderDescending().FirstOrDefault() : null;
        if (sdk != null)
        {
            foreach (string part in new[] { "ucrt", "shared", "um" }) args.AddRange(["-isystem", Path.Combine(sdk, part)]);
            foreach (string part in new[] { "ucrt", "um" }) args.AddRange(["-Xlinker", "/libpath:" + Path.Combine(kits, "Lib", Path.GetFileName(sdk), part, "x64")]);
        }
        return args;
    }

    internal static string GetObject(string driver, int reserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes)
    {
        if (reserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        return BuildObject(driver, "Exceptions", ["-DGFLAT_EXCEPTION_RESERVE_BYTES=" + reserveBytes]);
    }

    internal static string GetStdioObject(string driver) => BuildObject(driver, "Stdio", []);

    private static string BuildObject(string driver, string name, string[] options)
    {
        lock (BuildLock)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Runtime", name + ".cpp");
            if (!File.Exists(source)) throw new FileNotFoundException("The native runtime source is missing. Restore the compiler's Runtime directory.", source);
            string fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))[..16];
            string key = driver + "|" + name + "|" + fingerprint + "|" + string.Join("|", options);
            if (Objects.TryGetValue(key, out string? cached) && File.Exists(cached)) return cached;
            string buildId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
            string directory = Path.Combine(Path.GetTempPath(), "gflat-runtime", Environment.ProcessId.ToString());
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, name + "-" + buildId + (OperatingSystem.IsWindows() ? ".obj" : ".o"));
            var args = new List<string> { "--driver-mode=g++", "-std=c++17", "-O2", "-fexceptions", "-c", source, "-o", output };
            args.AddRange(options);
            args.AddRange(HostArguments());
            ProcessResult result = NativeToolchain.Run(driver, args, 60000);
            if (result.ExitCode != 0) throw new InvalidOperationException("Failed to compile the native runtime. Install the host C++ development tools.\n" + result.StandardError);
            Objects[key] = output;
            return output;
        }
    }
}
