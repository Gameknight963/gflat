using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace gflat;

internal static class NativeRuntimeCache
{
    internal const string OutputPlaceholder = "<gflat-runtime-output>";
    private sealed record Input(string Path, long Length, long Modified);
    private sealed record Entry(string Object, string Hash, Input[] Inputs);
    internal sealed record DriverIdentity(string Path, long Length, long Modified, string Hash);

    internal static string GetObject(string driver, string source, string identity, IReadOnlyList<string> arguments,
        string? cacheDirectory = null, DriverIdentity? driverIdentity = null)
    {
        driver = Path.GetFullPath(NativeToolchain.FindClang(driver));
        source = Path.GetFullPath(source);
        var observed = Observe(driver);
        if (driverIdentity?.Path != driver || driverIdentity.Length != observed.Length || driverIdentity.Modified != observed.Modified)
            driverIdentity = IdentifyDriver(driver);
        string key = BuildKey(driver, source, identity, arguments, driverIdentity);
        string directory = Path.GetFullPath(Path.Combine(cacheDirectory ?? CacheDirectory(), key));
        try
        {
            Directory.CreateDirectory(directory);
            using var cacheLock = Lock(Path.Combine(directory, "build.lock"));
            if (Read(directory) is { } cached) return cached;
            return Build(directory, driver, source, arguments, publish: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A read-only or unavailable cache still permits an ordinary build.
            string temporary = Path.Combine(Path.GetTempPath(), "gflat-runtime", Guid.NewGuid().ToString("N"));
            return Build(temporary, driver, source, arguments, publish: false);
        }
    }

    internal static DriverIdentity IdentifyDriver(string driver)
    {
        driver = Path.GetFullPath(NativeToolchain.FindClang(driver));
        var input = Observe(driver);
        return new(driver, input.Length, input.Modified, HashFile(driver));
    }

    internal static string BuildKey(string driver, string source, string identity, IReadOnlyList<string> arguments,
        DriverIdentity? driverIdentity = null)
    {
        driverIdentity ??= IdentifyDriver(driver);
        return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Version = 1, Identity = identity, Source = source, SourceHash = HashFile(source),
            Driver = new Input(driverIdentity.Path, driverIdentity.Length, driverIdentity.Modified), DriverHash = driverIdentity.Hash,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            arguments, Environment = CompilerEnvironment()
        })));
    }

    private static string CacheDirectory()
    {
        if (Environment.GetEnvironmentVariable("GFLAT_RUNTIME_CACHE_DIRECTORY") is { Length: > 0 } configured)
            return Path.GetFullPath(configured);
        string root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "gflat", "Cache", "native-runtime", "v1")
            : Path.Combine(root, "gflat", "native-runtime", "v1");
    }

    private static Dictionary<string, string?> CompilerEnvironment()
    {
        var result = new Dictionary<string, string?>();
        foreach (string name in new[] { "PATH", "INCLUDE", "LIB", "LIBPATH", "CPATH", "C_INCLUDE_PATH",
            "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH", "SDKROOT", "MACOSX_DEPLOYMENT_TARGET", "CL", "_CL_",
            "CCC_OVERRIDE_OPTIONS", "GCC_EXEC_PREFIX", "COMPILER_PATH", "CLANG_CONFIG_FILE_SYSTEM_DIR", "CLANG_CONFIG_FILE_USER_DIR" })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            result[name] = value;
            if (!string.IsNullOrEmpty(value) && (name is "CL" or "_CL_" or "CCC_OVERRIDE_OPTIONS" ||
                value.Split(Path.PathSeparator).Any(part => !Path.IsPathRooted(part.Trim('"')))))
                result["WorkingDirectory"] = Environment.CurrentDirectory;
        }
        return result;
    }

    private static FileStream Lock(string path)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 11 or 32 or 33 && timer.Elapsed < TimeSpan.FromMinutes(2))
            { Thread.Sleep(25); }
        }
    }

    private static string? Read(string directory)
    {
        string manifest = Path.Combine(directory, "current.json");
        if (!File.Exists(manifest)) return null;
        Entry? entry;
        try { entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(manifest)); }
        catch (JsonException) { return null; }
        if (entry?.Object == null || entry.Hash == null || entry.Inputs == null || entry.Inputs.Length == 0) return null;
        string output;
        try { output = Path.GetFullPath(Path.Combine(directory, entry.Object)); }
        catch (ArgumentException) { return null; }
        if (!output.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(output) || entry.Inputs.Any(input => input == null || !File.Exists(input.Path) || Observe(input.Path) != input))
            return null;
        return HashFile(output) == entry.Hash ? output : null;
    }

    private static string Build(string directory, string driver, string source, IReadOnlyList<string> arguments, bool publish)
    {
        string id = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(directory, ".build-" + id);
        Directory.CreateDirectory(staging);
        string output = Path.Combine(staging, OperatingSystem.IsWindows() ? "runtime.obj" : "runtime.o");
        string dependencies = Path.Combine(staging, "runtime.d");
        DateTime started = DateTime.UtcNow;
        try
        {
            var command = arguments.Select(argument => argument == OutputPlaceholder ? output : argument).ToList();
            command.AddRange(["-MD", "-MF", dependencies, "-MT", "runtime"]);
            using (PipelineTimings.Measure("native.runtime-build", Path.GetFileName(source)))
            {
                var result = NativeToolchain.Run(driver, command, 60000);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to compile {Path.GetFileName(source)} native runtime.\n" + result.StandardError);
            }
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("The native compiler did not produce a runtime object.");
            Input[] inputs = Dependencies(File.ReadAllText(dependencies)).Append(source).Append(driver)
                .Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Select(Observe).ToArray();
            string completed = Path.Combine(directory, id);
            Directory.Move(staging, completed);
            output = Path.Combine(completed, Path.GetFileName(output));
            // A header changed during compilation cannot certify a reusable object.
            if (publish && inputs.All(input => input.Modified <= started.Ticks))
            {
                var entry = new Entry(Path.GetRelativePath(directory, output), HashFile(output), inputs);
                string temporary = Path.Combine(directory, id + ".json");
                File.WriteAllText(temporary, JsonSerializer.Serialize(entry));
                File.Move(temporary, Path.Combine(directory, "current.json"), overwrite: true);
            }
            return output;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static IEnumerable<string> Dependencies(string text)
    {
        text = text.Replace("\\\r\n", " ").Replace("\\\n", " ");
        int colon = text.IndexOf(':');
        if (colon < 0) throw new InvalidOperationException("The native compiler produced an invalid dependency file.");
        var path = new StringBuilder();
        for (int i = colon + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && (char.IsWhiteSpace(text[i + 1]) || text[i + 1] is '\\' or '#' or ':'))
                path.Append(text[++i]);
            else if (c == '$' && i + 1 < text.Length && text[i + 1] == '$') { path.Append('$'); i++; }
            else if (char.IsWhiteSpace(c))
            {
                if (path.Length > 0) { yield return path.ToString(); path.Clear(); }
            }
            else path.Append(c);
        }
        if (path.Length > 0) yield return path.ToString();
    }

    private static Input Observe(string path)
    {
        var file = new FileInfo(path);
        return new(path, file.Length, file.LastWriteTimeUtc.Ticks);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
