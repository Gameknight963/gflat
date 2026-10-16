using gflat.CompileExceptions;
using gflat.diagnostics;
using gflat.Projects;

namespace gflat;

public static class Program
{
    public static int Main(string[] args)
    {
        var diagnostics = new DiagnosticBag();
        try
        {
            if (args.Length > 0 && args[0] == "sdk") return ProjectSdkInstallation.Run(args[1..]);
            if (args.Length > 0 && args[0] == "new") return ProjectCreation.Run(args[1..]);
            if (args.Length > 0 && args[0] == "doctor") return ToolchainDoctor.Run(args[1..]);
            int argumentSeparator = Array.IndexOf(args, "--");
            string[] programArguments = argumentSeparator < 0 ? [] : args[(argumentSeparator + 1)..];
            if (argumentSeparator >= 0) args = args[..argumentSeparator];
            var sourcePaths = new List<string>();
            string? outputPath = null, clang = null;
            string configuration = "Debug", platform = "AnyCPU";
            bool run = false, irOnly = false, check = false, clean = false, noStdLib = false;
            bool explicitConfiguration = false, explicitPlatform = false;
            string? projectPath = null;
            int? exceptionReserveBytes = null;
            int? optimizationLevel = null;
            for (int i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
                switch (args[i])
                {
                    case "--help": case "-h":
                        Console.WriteLine("gflat new <directory> [--name Name] [--library] [--no-stdlib]\nOptimization: --optimization 0|1|2|3 (project defaults: Debug=0, Release=2)");
                        Console.WriteLine("gflat doctor [--native] [--clang path]");
                        Console.WriteLine("gflat sdk install [--sdk-directory path]");
                        Console.WriteLine("gflat <source.gf> [more.gf ...] [-o output] [--emit-ir] [--run] [--check] [--clang path] [--target <host-target-triple>] [--exception-reserve-bytes N] [--no-stdlib]\ngflat [build|check|clean] <project.gfproj> [--configuration Debug|Release] [--platform AnyCPU] [--run] [-o output] [--emit-ir]\ngflat --source-list <file> [compiler options]\nPass program arguments after --run -- (for example: --run -- input.txt \"some text\").");
                        return 0;
                    case "build" when i == 0: break;
                    case "check" when i == 0: check = true; break;
                    case "clean" when i == 0: clean = true; break;
                    case "--check": check = true; break;
                    case "--no-stdlib": noStdLib = true; break;
                    case "-o": outputPath = Value(); break;
                    case "--clang": clang = Value(); break;
                    case "--exception-reserve-bytes": exceptionReserveBytes = ExceptionReserve.Parse(Value()); break;
                    case "--optimization": optimizationLevel = OptimizationLevel.Parse(Value()); break;
                    case "--configuration": configuration = Value(); explicitConfiguration = true; break;
                    case "--platform": platform = Value(); explicitPlatform = true; break;
                    case "--source-list": sourcePaths.AddRange(File.ReadAllLines(Value()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)); break;
                    case "--target": TargetInfo.Parse(Value()); break;
                    case "--run": run = true; break;
                    case "--emit-ir": irOnly = true; break;
                    default:
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unexpected argument '{args[i]}'");
                        sourcePaths.Add(Path.GetFullPath(args[i])); break;
                }
            }
            if (argumentSeparator >= 0 && !run)
                throw new ArgumentException("Program arguments after '--' require --run.");
            ProjectGraph? graph = null;
            if (sourcePaths.Any(p => p.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourcePaths.Count != 1) throw new ArgumentException("Pass one project file, without extra source files.");
                projectPath = sourcePaths[0];
                var root = ProjectModel.Load(projectPath, explicitConfiguration ? configuration : null,
                    explicitPlatform ? platform : null, exceptionReserveBytes, optimizationLevel, noStdLib ? true : null);
                configuration = root.Configuration;
                platform = root.Platform;
                graph = ProjectGraph.Load(projectPath, (path, asReference) => !asReference ? root
                    : ProjectModel.Load(path, configuration, platform, exceptionReserveBytes, optimizationLevel, noStdLib ? true : null, asReference: true));
                sourcePaths = graph.Sources.ToList();
                if (graph.Root.Kind == ProjectKind.Executable)
                    outputPath ??= irOnly ? Path.ChangeExtension(graph.Root.TargetPath, ".ll") : graph.Root.TargetPath;
                if (graph.Root.Kind == ProjectKind.SourceLibrary)
                {
                    if (run || irOnly || args.Contains("-o")) throw new ArgumentException("SourceLibrary projects have no standalone output; build an executable referencing them.");
                }
            }
            if (clean)
            {
                if (graph == null || run || irOnly || args.Contains("-o")) throw new ArgumentException("Use clean with a project file and no output options.");
                // The SDK validates the actual executable/IR paths against all
                // sources and imports before deleting those same normalized paths.
                return ProjectBuild.Run(graph.Root, "Clean", configuration, platform, sourceGraph: graph, noStdLib: noStdLib);
            }
            if (sourcePaths.Count == 0 && graph == null) throw new ArgumentException("Specify a source file. Use --help for usage.");
            if (check && (run || irOnly)) throw new ArgumentException("--check cannot be combined with --run or --emit-ir.");
            if (run && irOnly) throw new ArgumentException("--run cannot be combined with --emit-ir.");
            if (graph?.Root.Kind == ProjectKind.SourceLibrary)
                return ProjectBuild.Run(graph.Root, check ? "Check" : "Build", configuration, platform,
                    clang: clang, exceptionReserveBytes: exceptionReserveBytes, optimizationLevel: optimizationLevel,
                    sourceGraph: graph, noStdLib: noStdLib);
            outputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(sourcePaths[0], irOnly ? ".ll" : ".exe"));
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (sourcePaths.Distinct(pathComparer).Count() != sourcePaths.Count)
                throw new ArgumentException("A source file may only be supplied once.");
            if (sourcePaths.Contains(outputPath, pathComparer) || graph?.Projects.Any(p => pathComparer.Equals(p.FilePath, outputPath)) == true)
                throw new ArgumentException("Output must differ from every source file.");
            if (graph != null)
            {
                int result = ProjectBuild.Run(graph.Root, check ? "Check" : "Build", configuration, platform,
                    irOnly, args.Contains("-o") ? outputPath : null, clang, exceptionReserveBytes, optimizationLevel, graph, noStdLib);
                if (result != 0 || !run) return result;
                return NativeToolchain.RunProgram(outputPath, programArguments);
            }
            var sources = sourcePaths.Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
            if (check)
            {
                Compiler.Check(sources, diagnostics, new() { NoStdLib = noStdLib });
                if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
                return 0;
            }
            string ir = Compiler.Emit(sources, diagnostics, new() { NoStdLib = noStdLib });
            if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
            if (projectPath != null) Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (irOnly) { File.WriteAllText(outputPath, ir); return 0; }
            string temporaryIr = Path.Combine(Path.GetTempPath(), "gflat_" + Guid.NewGuid().ToString("N") + ".ll");
            try
            {
                File.WriteAllText(temporaryIr, ir);
                ProcessResult compiled = NativeToolchain.Compile(temporaryIr, outputPath, clang, exceptionReserveBytes ?? ExceptionReserve.DefaultBytes, optimizationLevel);
                if (compiled.ExitCode != 0) { Console.Error.WriteLine("Native toolchain error: " + compiled.StandardError); return 2; }
                if (!run) return 0;
                return NativeToolchain.RunProgram(outputPath, programArguments);
            }
            finally { File.Delete(temporaryIr); }
        }
        catch (ProjectException ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }
        catch (TypeCheckException ex)
        {
            Console.Error.WriteLine(diagnostics.HasErrors ? diagnostics.FormatAll() : ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Internal compiler error: {ex}");
            return 3;
        }
    }
}
