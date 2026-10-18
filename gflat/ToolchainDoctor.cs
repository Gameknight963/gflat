using gflat.Projects;

namespace gflat;

/// <summary>Checks the installed tools through the same paths used by real builds.</summary>
public static class ToolchainDoctor
{
    public static int Run(string[] args)
    {
        string? clang = null;
        bool native = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--native": native = true; break;
                case "--clang":
                    clang = ++i < args.Length ? args[i] : throw new ArgumentException("Missing value for --clang.");
                    break;
                case "--help": case "-h":
                    Console.WriteLine("gflat doctor [--native] [--clang path]");
                    Console.WriteLine("Check SDK, project evaluation, and Clang. --native also builds and runs an exception smoke test.");
                    return 0;
                default: throw new ArgumentException($"Unexpected doctor argument '{args[i]}'.");
            }
        }

        Console.WriteLine($"Host: {TargetInfo.Default.Triple}");
        Console.WriteLine($".NET runtime: {Environment.Version}");
        Console.WriteLine($"Compiler directory: {AppContext.BaseDirectory}");
        bool failed = false;
        void Check(string name, Func<string> action)
        {
            try { Console.WriteLine($"{name}: {action()}"); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failed = true;
                Console.Error.WriteLine($"{name}: FAILED — {error.Message}");
            }
        }

        string scratch = Path.Combine(Path.GetTempPath(), "gflat-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            Check("SDK", ProjectCreation.FindToolchainRoot);
            Check("MSBuild project evaluation", () =>
            {
                string path = ProjectCreation.Create(Path.Combine(scratch, "project"), "Probe", library: true, useStd: false);
                ProjectModel.Load(path);
                return "OK (bundled engine: " + Path.Combine(AppContext.BaseDirectory, "MSBuild.dll") + ")";
            });
            Check("Clang", () =>
            {
                string driver = NativeToolchain.FindClang(clang);
                ProcessResult result = NativeToolchain.Run(driver, ["--version"]);
                if (result.ExitCode != 0) throw new IOException(result.StandardError.Trim());
                return driver + Environment.NewLine + "  " + result.StandardOutput.Split('\n')[0].Trim();
            });
            if (native) Check("Native build and exception unwinding", () =>
            {
                const string source = "int main() { try { throw new Exception(); } catch (ref Exception error) { return 0; } return 1; }";
                string ir = Path.Combine(scratch, "probe.ll");
                string executable = Path.Combine(scratch, "probe.exe");
                File.WriteAllText(ir, Compiler.Emit(source));
                ProcessResult build = NativeToolchain.Compile(ir, executable, clang);
                if (build.ExitCode != 0) throw new IOException(build.StandardError.Trim());
                ProcessResult run = NativeToolchain.Run(executable, []);
                if (run.ExitCode != 0) throw new IOException($"Probe exited with {run.ExitCode}. {run.StandardError.Trim()}");
                return "OK";
            });
        }
        finally { Directory.Delete(scratch, recursive: true); }
        return failed ? 2 : 0;
    }
}
