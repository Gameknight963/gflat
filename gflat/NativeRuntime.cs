using System.Security.Cryptography;

namespace gflat;

/// <summary>Build native C stream accessors and the host C++ exception transport.</summary>
internal static class NativeRuntime
{
    private static readonly object BuildLock = new();
    private static readonly Dictionary<string, string> Objects = new();

    internal static List<string> HostArguments(bool requireCppHeaders = true)
    {
        if (!OperatingSystem.IsWindows()) return [];
        string kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
        return HostArguments(NativeToolchain.VisualStudioInstallations(), kits, requireCppHeaders);
    }

    internal static List<string> HostArguments(IEnumerable<string> installations, string kits, bool requireCppHeaders = true)
    {
        var args = new List<string>();
        string? toolset = NativeToolchain.FindMsvcToolsetDirectory(installations, requireHeaders: true, requireCppHeaders: requireCppHeaders);
        if (toolset != null)
            args.AddRange(["-isystem", Path.Combine(toolset, "include"), "-Xlinker", "/libpath:" + Path.Combine(toolset, "lib", "x64")]);
        string? sdk = FindWindowsSdkDirectory(kits);
        if (sdk != null)
        {
            foreach (string part in new[] { "ucrt", "shared", "um" }) args.AddRange(["-isystem", Path.Combine(sdk, part)]);
            foreach (string part in new[] { "ucrt", "um" }) args.AddRange(["-Xlinker", "/libpath:" + Path.Combine(kits, "Lib", Path.GetFileName(sdk), part, "x64")]);
        }
        return args;
    }

    private static string? FindWindowsSdkDirectory(string kits)
    {
        string includes = Path.Combine(kits, "Include");
        if (!Directory.Exists(includes)) return null;
        return Directory.EnumerateDirectories(includes)
            .Select(directory => (
                Version: Version.TryParse(Path.GetFileName(directory), out var version) ? version : null,
                Directory: directory))
            .Where(candidate => candidate.Version != null &&
                File.Exists(Path.Combine(candidate.Directory, "ucrt", "stdio.h")) &&
                File.Exists(Path.Combine(candidate.Directory, "shared", "sdkddkver.h")) &&
                File.Exists(Path.Combine(candidate.Directory, "um", "windows.h")) &&
                File.Exists(Path.Combine(kits, "Lib", Path.GetFileName(candidate.Directory), "ucrt", "x64", "ucrt.lib")) &&
                File.Exists(Path.Combine(kits, "Lib", Path.GetFileName(candidate.Directory), "um", "x64", "kernel32.lib")))
            .OrderByDescending(candidate => candidate.Version)
            .ThenBy(candidate => candidate.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Directory).FirstOrDefault();
    }
    internal static string GetObject(string driver, int reserveBytes = gflat.Projects.ExceptionReserve.DefaultBytes)
    {
        if (reserveBytes < 0) throw new ArgumentOutOfRangeException(nameof(reserveBytes));
        return BuildObject(driver, "Exceptions", cpp: true, cppHost: true, ["-DGFLAT_EXCEPTION_RESERVE_BYTES=" + reserveBytes]);
    }

    internal static string GetStdioObject(string driver, bool cppHost = false) => BuildObject(driver, "Stdio", cpp: false, cppHost, []);

    private static string BuildObject(string driver, string name, bool cpp, bool cppHost, string[] options)
    {
        lock (BuildLock)
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Runtime", name + (cpp ? ".cpp" : ".c"));
            if (!File.Exists(source)) throw new FileNotFoundException("The native runtime source is missing. Restore the compiler's Runtime directory.", source);
            string fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))[..16];
            string key = ObjectCacheKey(driver, name, fingerprint, cpp, cppHost, options);
            if (Objects.TryGetValue(key, out string? cached) && File.Exists(cached)) return cached;
            string buildId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16];
            string directory = Path.Combine(Path.GetTempPath(), "gflat-runtime", Environment.ProcessId.ToString());
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, name + "-" + buildId + (OperatingSystem.IsWindows() ? ".obj" : ".o"));
            var args = ObjectArguments(source, output, cpp, options, HostArguments(requireCppHeaders: cppHost));
            ProcessResult result = NativeToolchain.Run(driver, args, 60000);
            if (result.ExitCode != 0) throw new InvalidOperationException($"Failed to compile {name} native runtime. Install the host {(cpp ? "C++" : "C")} development tools.\n" + result.StandardError);
            Objects[key] = output;
            return output;
        }
    }
    internal static string ObjectCacheKey(string driver, string name, string fingerprint, bool cpp, bool cppHost, IEnumerable<string> options)
        => driver + "|" + name + "|" + fingerprint + "|" + (cpp ? "cpp" : "c") + "|" +
            (cppHost ? "cpp-host" : "c-host") + "|" + string.Join("|", options);

    internal static List<string> ObjectArguments(string source, string output, bool cpp, IEnumerable<string> options, IEnumerable<string> hostArguments)
    {
        var args = new List<string> { cpp ? "--driver-mode=g++" : "--driver-mode=gcc", cpp ? "-std=c++17" : "-std=c11", "-O2" };
        if (cpp) args.Add("-fexceptions");
        args.AddRange(["-ffunction-sections", "-fdata-sections", "-c", source, "-o", output]);
        args.AddRange(options);
        args.AddRange(hostArguments);
        return args;
    }

}
