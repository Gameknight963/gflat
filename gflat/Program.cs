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
            if (args.Length > 0 && args[0] == "--msbuild") return ProjectBuild.RunEngine(args[1..]);
            if (args.Length > 0 && args[0] == "new") return ProjectCreation.Run(args[1..]);
            if (args.Length > 0 && args[0] == "doctor") return ToolchainDoctor.Run(args[1..]);
            int argumentSeparator = Array.IndexOf(args, "--");
            string[] programArguments = argumentSeparator < 0 ? [] : args[(argumentSeparator + 1)..];
            if (argumentSeparator >= 0) args = args[..argumentSeparator];
            var sourcePaths = new List<string>();
            string? outputPath = null, clang = null;
            string configuration = "Debug", platform = "AnyCPU";
            string outputType = "Executable";
            bool run = false, irOnly = false, check = false, clean = false, noStdLib = false;
            bool explicitConfiguration = false, explicitPlatform = false;
            string? projectPath = null;
            int? exceptionReserveBytes = null;
            int? optimizationLevel = null;
            bool? debugSymbols = null;
            bool? noBuiltins = null;
            string? runtimeLibrary = null;
            NativeLinkInputs nativeLinkInputs = NativeLinkInputs.Empty;
            var nativeManifests = new List<string>();
            var sourceLists = new List<string>();
            var binaryInterfaces = new List<string>();
            var runtimeFiles = new List<string>();
            List<string>? exportSources = null;
            PreprocessorInputs preprocessing = PreprocessorInputs.Empty;
            var preprocessorManifests = new List<string>();
            string? dependencyFile = null;
            void IncludeDirectory(string value) => preprocessing = preprocessing.Append(new([Path.GetFullPath(value)], []));
            void Define(string value) => preprocessing = preprocessing.Append(new([], [value]));
            void AddLibrary(string kind, string value) => nativeLinkInputs = nativeLinkInputs.Append(
                NativeLinkInputs.Create([new NativeLibraryInput(kind, value)], [], Environment.CurrentDirectory));
            for (int i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
                switch (args[i])
                {
                    case "--help": case "-h":
                        Console.WriteLine("Preprocessing: -I PATH / --include-directory PATH, -D NAME[=TOKENS] / --define NAME[=TOKENS].");
                        Console.WriteLine("gflat new <directory> [--name Name] [--library] [--no-stdlib]\nOptimization: --optimization 0|1|2|3 (project defaults: Debug=0, Release=2)");
                        Console.WriteLine("gflat doctor [--native] [--clang path]");
                        Console.WriteLine("Debug symbols: -g or --debug; --no-debug disables the project's DebugSymbols setting.");
                        Console.WriteLine("Libc builtin recognition: --no-builtins disables it; --builtins overrides the project's NoBuiltins setting. Explicit calls/intrinsics may still require runtime helpers.");
                        Console.WriteLine("Native linking: repeat --native-library NAME, --native-library-file PATH, and --native-library-dir PATH. Project options append to declared requirements.");
                        Console.WriteLine("Source builds include Standard unless --no-stdlib is specified. Use gfc for explicit compiler inputs. Pass project files directly to gflat.");
                        Console.WriteLine("Binary output: --output-type Executable|StaticLibrary|DynamicLibrary. Projects use the OutputType property.");
                        Console.WriteLine("gflat <source.gf> [more.gf ...] [-o output] [--emit-ir] [--run] [--check] [--clang path] [--target <host-target-triple>] [--exception-reserve-bytes N] [--no-stdlib]\ngflat [build|check|clean] <project.gfproj> [--configuration Debug|Release] [--platform AnyCPU] [--run] [-o output] [--emit-ir]\ngflat --source-list <file> [compiler options]\nPass program arguments after --run -- (for example: --run -- input.txt \"some text\").");
                        return 0;
                    case "build" when i == 0: break;
                    case "check" when i == 0: check = true; break;
                    case "clean" when i == 0: clean = true; break;
                    case "--check": check = true; break;
                    case "--no-stdlib": noStdLib = true; break;
                    case "-g": case "--debug": debugSymbols = true; break;
                    case "--no-debug": debugSymbols = false; break;
                    case "--no-builtins": noBuiltins = true; break;
                    case "--builtins": noBuiltins = false; break;
                    case "--runtime-library": runtimeLibrary = RuntimeLibrary.Parse(Value()); break;
                    case "-o": outputPath = Value(); break;
                    case "--output-type":
                        outputType = Value();
                        if (outputType is not ("Executable" or "StaticLibrary" or "DynamicLibrary"))
                            throw new ArgumentException("Output type must be Executable, StaticLibrary, or DynamicLibrary.");
                        break;
                    case "--clang": clang = Value(); break;
                    case "--exception-reserve-bytes": exceptionReserveBytes = ExceptionReserve.Parse(Value()); break;
                    case "--optimization": optimizationLevel = OptimizationLevel.Parse(Value()); break;
                    case "--configuration": configuration = Value(); explicitConfiguration = true; break;
                    case "--platform": platform = Value(); explicitPlatform = true; break;
                    case "--source-list":
                        string sourceList = Path.GetFullPath(Value());
                        sourceLists.Add(sourceList);
                        sourcePaths.AddRange(File.ReadAllLines(sourceList).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath));
                        break;
                    case "--library-interface": binaryInterfaces.Add(Path.GetFullPath(Value())); break;
                    case "--runtime-file": runtimeFiles.Add(Path.GetFullPath(Value())); break;
                    case "--export-source-list":
                        string exportList = Path.GetFullPath(Value());
                        sourceLists.Add(exportList);
                        exportSources = File.ReadAllLines(exportList).Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToList();
                        break;
                    case "-I": case "--include-directory": IncludeDirectory(Value()); break;
                    case "-D": case "--define": Define(Value()); break;
                    case "--preprocessor-inputs":
                        string preprocessingManifest = Path.GetFullPath(Value());
                        preprocessorManifests.Add(preprocessingManifest);
                        preprocessing = PreprocessorInputs.Read(preprocessingManifest).Append(preprocessing);
                        break;
                    case "--dependencies": dependencyFile = Path.GetFullPath(Value()); break;
                    case "--native-library": AddLibrary("Name", Value()); break;
                    case "--native-library-file": AddLibrary("File", Value()); break;
                    case "--native-library-dir": nativeLinkInputs = nativeLinkInputs.Append(NativeLinkInputs.Create([], [Value()], Environment.CurrentDirectory)); break;
                    case "--native-link-inputs":
                        string manifest = Path.GetFullPath(Value());
                        nativeManifests.Add(manifest);
                        nativeLinkInputs = nativeLinkInputs.Append(NativeLinkInputs.Read(manifest));
                        break;
                    case "--target": TargetInfo.Parse(Value()); break;
                    case "--run": run = true; break;
                    case "--emit-ir": irOnly = true; break;
                    default:
                        if (args[i].StartsWith("-I", StringComparison.Ordinal) && args[i].Length > 2) { IncludeDirectory(args[i][2..]); break; }
                        if (args[i].StartsWith("-D", StringComparison.Ordinal) && args[i].Length > 2) { Define(args[i][2..]); break; }
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unexpected argument '{args[i]}'");
                        sourcePaths.Add(Path.GetFullPath(args[i])); break;
                }
            }
            if (argumentSeparator >= 0 && !run)
                throw new ArgumentException("Program arguments after '--' require --run.");
            ProjectGraph? graph = null;
            if (sourcePaths.Any(p => p.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourceLists.Count != 0)
                    throw new ArgumentException("--source-list is a source-build option. Pass project files directly.");
                if (nativeManifests.Count != 0)
                    throw new ArgumentException("--native-link-inputs is an internal source-build option. Use --native-library, --native-library-file, and --native-library-dir with projects.");
                if (preprocessorManifests.Count != 0 || dependencyFile != null)
                    throw new ArgumentException("--preprocessor-inputs and --dependencies are internal source-build options. Use -I and -D with projects.");
                if (sourcePaths.Count != 1) throw new ArgumentException("Pass one project file, without extra source files.");
                projectPath = sourcePaths[0];
                var root = ProjectModel.Load(projectPath, explicitConfiguration ? configuration : null,
                    explicitPlatform ? platform : null, exceptionReserveBytes, optimizationLevel, noStdLib ? true : null, debugSymbols: debugSymbols, noBuiltins: noBuiltins);
                if (runtimeLibrary != null && root.RuntimeLibrary != runtimeLibrary)
                    throw new ArgumentException("Set RuntimeLibrary in the project for a project build.");
                configuration = root.Configuration;
                platform = root.Platform;
                graph = ProjectGraph.Load(projectPath, (path, asReference) => !asReference ? root
                    : ProjectModel.Load(path, configuration, platform, exceptionReserveBytes, optimizationLevel, noStdLib ? true : null, asReference: true, debugSymbols: debugSymbols, noBuiltins: noBuiltins));
                sourcePaths = graph.Sources.ToList();
                if (graph.Root.Kind != ProjectKind.SourceLibrary)
                {
                    if (args.Contains("--output-type") && outputType != graph.Root.Kind.ToString())
                        throw new ArgumentException("Set OutputType in the project rather than overriding its artifact type on the command line.");
                    outputType = graph.Root.Kind.ToString();
                    outputPath ??= irOnly ? Path.ChangeExtension(graph.Root.TargetPath, ".ll") : graph.Root.TargetPath;
                }
                if (graph.Root.Kind == ProjectKind.SourceLibrary)
                {
                    if (args.Contains("--output-type")) throw new ArgumentException("Set OutputType in the project to build a binary library.");
                    if (run || irOnly || args.Contains("-o")) throw new ArgumentException("SourceLibrary projects have no standalone output; build an executable referencing them.");
                }
            }
            if (clean)
            {
                if (graph == null || run || irOnly || args.Contains("-o")) throw new ArgumentException("Use clean with a project file and no output options.");
                // The SDK validates the actual executable/IR paths against all
                // sources and imports before deleting those same normalized paths.
                return ProjectBuild.Run(graph.Root, "Clean", configuration, platform, sourceGraph: graph, noStdLib: noStdLib, noBuiltins: noBuiltins, nativeLinkOverrides: nativeLinkInputs);
            }
            if (sourcePaths.Count == 0 && graph == null) throw new ArgumentException("Specify a source file. Use --help for usage.");
            if (check && (run || irOnly)) throw new ArgumentException("--check cannot be combined with --run or --emit-ir.");
            if (run && irOnly) throw new ArgumentException("--run cannot be combined with --emit-ir.");
            if (run && outputType != "Executable") throw new ArgumentException("--run requires executable output.");
            if (graph?.Root.Kind == ProjectKind.SourceLibrary)
                return ProjectBuild.Run(graph.Root, check ? "Check" : "Build", configuration, platform,
                    clang: clang, exceptionReserveBytes: exceptionReserveBytes, optimizationLevel: optimizationLevel,
                    sourceGraph: graph, noStdLib: noStdLib, noBuiltins: noBuiltins, nativeLinkOverrides: nativeLinkInputs, preprocessorOverrides: preprocessing);
            string extension = outputType switch { "StaticLibrary" => OperatingSystem.IsWindows() ? ".lib" : ".a",
                "DynamicLibrary" => OperatingSystem.IsWindows() ? ".dll" : ".so", _ => ".exe" };
            outputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(sourcePaths[0], irOnly ? ".ll" : extension));
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (string path in binaryInterfaces)
            {
                var metadata = System.Xml.Linq.XElement.Load(path);
                string library = (string?)metadata.Attribute("Library") ?? throw new InvalidDataException("Library interface is missing its artifact path.");
                string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, library));
                var reference = new BinaryProjectReference(target, (string?)metadata.Attribute("Kind") == "DynamicLibrary");
                if (runtimeLibrary != null && reference.RuntimeLibrary != null && runtimeLibrary != reference.RuntimeLibrary)
                    throw new ArgumentException("Referenced libraries use incompatible RuntimeLibrary settings.");
                runtimeLibrary ??= reference.RuntimeLibrary;
                nativeLinkInputs = nativeLinkInputs.Append(new(reference.NativeInputs.Libraries.Where(required =>
                    !nativeLinkInputs.Libraries.Any(existing => existing.Kind == required.Kind && pathComparer.Equals(existing.Value, required.Value))), reference.NativeInputs.Directories));
                runtimeFiles.AddRange(reference.RuntimeFiles);
            }
            runtimeFiles = runtimeFiles.Distinct(pathComparer).ToList();
            runtimeLibrary ??= "MultiThreaded";
            if (runtimeFiles.GroupBy(Path.GetFileName, pathComparer).Any(group => group.Count() > 1))
                throw new ArgumentException("Referenced runtime libraries have conflicting filenames.");
            if (sourcePaths.Distinct(pathComparer).Count() != sourcePaths.Count)
                throw new ArgumentException("A source file may only be supplied once.");
            void ValidateOutput(string path)
            {
                if (sourcePaths.Contains(path, pathComparer) || sourceLists.Contains(path, pathComparer) ||
                    graph?.Projects.Any(project => pathComparer.Equals(project.FilePath, path)) == true)
                    throw new ArgumentException("Output must differ from every source file and source list.");
                if (nativeLinkInputs.Libraries.Any(library => library.Kind == "File" && pathComparer.Equals(library.Value, path)) ||
                    nativeManifests.Contains(path, pathComparer) || preprocessorManifests.Contains(path, pathComparer) || binaryInterfaces.Contains(path, pathComparer) || runtimeFiles.Contains(path, pathComparer))
                    throw new ArgumentException("Output must differ from native library files and link manifests.");
            }
            ValidateOutput(outputPath);
            if (irOnly) ValidateOutput(outputPath + ".foreign.json");
            if (dependencyFile != null)
            {
                ValidateOutput(dependencyFile);
                ValidateOutput(dependencyFile + ".state");
                if (pathComparer.Equals(dependencyFile, outputPath) || pathComparer.Equals(dependencyFile + ".state", outputPath)) throw new ArgumentException("Dependencies and output must use different paths.");
                if (irOnly && (pathComparer.Equals(dependencyFile, outputPath + ".foreign.json") || pathComparer.Equals(dependencyFile + ".state", outputPath + ".foreign.json")))
                    throw new ArgumentException("Dependencies and foreign manifest output must use different paths.");
            }
            if (graph != null)
            {
                int result = ProjectBuild.Run(graph.Root, check ? "Check" : "Build", configuration, platform,
                    irOnly, args.Contains("-o") ? outputPath : null, clang, exceptionReserveBytes, optimizationLevel, graph, noStdLib, debugSymbols, noBuiltins, nativeLinkInputs, preprocessing);
                if (result != 0 || !run) return result;
                return NativeToolchain.RunProgram(outputPath, programArguments);
            }
            var compilerArguments = args.ToList();
            if (compilerArguments[0] is "build" or "check")
            {
                string command = compilerArguments[0];
                compilerArguments.RemoveAt(0);
                if (command == "check") compilerArguments.Add("--check");
            }
            if (!noStdLib)
            {
                IEnumerable<string> librarySources = sourceLists.Count != 0
                    ? StandardLibrary.AutomaticFiles.Select(relative => StandardLibrary.Load(relative).Path)
                    : Directory.EnumerateFiles(Path.Combine(ProjectCreation.FindToolchainRoot(), "Standard"), "*.gf", SearchOption.AllDirectories);
                var supplied = sourcePaths.ToHashSet(pathComparer);
                compilerArguments.AddRange(librarySources.Where(supplied.Add));
            }
            if (programArguments.Length > 0) { compilerArguments.Add("--"); compilerArguments.AddRange(programArguments); }
            return CompilerProgram.Run(compilerArguments.ToArray(), sourcePaths);
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
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException)
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
