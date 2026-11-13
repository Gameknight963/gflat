using gflat.CompileExceptions;
using gflat.diagnostics;
using gflat.Projects;

namespace gflat;

public static class CompilerProgram
{
    public static int Main(string[] args)
        => Run(args);

    public static int Run(string[] args, IReadOnlyList<string>? defaultExportSources = null)
    {
        var diagnostics = new DiagnosticBag();
        try
        {
            int argumentSeparator = Array.IndexOf(args, "--");
            string[] programArguments = argumentSeparator < 0 ? [] : args[(argumentSeparator + 1)..];
            if (argumentSeparator >= 0) args = args[..argumentSeparator];
            var sourcePaths = new List<string>();
            string? outputPath = null, clang = null;
            string outputType = "Executable";
            bool run = false, irOnly = false, check = false, noStdLib = true;
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
                        Console.WriteLine("gfc <source.gf> [more.gf ...] [-o output] [--check] [--emit-ir] [--run]");
                        Console.WriteLine("Explicit compiler inputs: --source-list FILE, -I PATH, -D NAME[=TOKENS], --library-interface FILE.");
                        Console.WriteLine("Native options: --clang PATH, --optimization 0|1|2|3, --debug, --no-builtins, --output-type Executable|StaticLibrary|DynamicLibrary.");
                        Console.WriteLine("Sources and declarations are supplied explicitly. Use gflat for projects and automatic Standard references.");
                        return 0;
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
            if (sourcePaths.Count == 0) throw new ArgumentException("Specify a source file. Use --help for usage.");
            if (sourcePaths.Any(path => path.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("gfc accepts explicit source inputs. Use gflat build for project files.");
            if (check && (run || irOnly)) throw new ArgumentException("--check cannot be combined with --run or --emit-ir.");
            if (run && irOnly) throw new ArgumentException("--run cannot be combined with --emit-ir.");
            if (run && outputType != "Executable") throw new ArgumentException("--run requires executable output.");
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
                if (sourcePaths.Contains(path, pathComparer) || sourceLists.Contains(path, pathComparer))
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
            var sources = sourcePaths.Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
            var included = new HashSet<string>(pathComparer);
            var compilationOptions = new CompilationOptions
            {
                NoStdLib = noStdLib, NoBuiltins = noBuiltins == true, InternalizeFunctions = !irOnly, DebugSymbols = debugSymbols == true,
                BinaryInterfaces = binaryInterfaces,
                ExportSources = outputType == "Executable" ? null : exportSources ?? defaultExportSources ?? sourcePaths,
                IncludeDirectories = preprocessing.Directories, Defines = preprocessing.Defines,
                FileDeclarationScopes = preprocessing.DeclarationScopes.ToDictionary(pair => pair.Key,
                    pair => Enum.Parse<DeclarationScope>(pair.Value), pathComparer),
                ForeignCompilerResolver = () => NativeToolchain.FindClang(clang),
                ForeignCompilerArguments = NativeRuntime.HostCompilerArguments(cpp: true, runtimeLibrary),
                DependencyObserver = paths =>
                {
                    included.UnionWith(paths);
                    if (included.Contains(outputPath) || irOnly && included.Contains(outputPath + ".foreign.json") || dependencyFile != null && (included.Contains(dependencyFile) || included.Contains(dependencyFile + ".state")))
                        throw new ArgumentException("Output must differ from included source files.");
                }
            };
            void WriteDependencies()
            {
                if (dependencyFile != null) PreprocessorInputs.WriteDependencies(dependencyFile, included);
            }
            if (check)
            {
                Compiler.Check(sources, diagnostics, compilationOptions);
                WriteDependencies();
                if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
                return 0;
            }
            var program = Compiler.EmitProgram(sources, diagnostics, compilationOptions);
            string ir = program.Ir;
            if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
            // Use the native toolchain's emitted-IR predicate to validate its PDB output.
            if (OperatingSystem.IsWindows() && !irOnly && NativeToolchain.HasDebugInformation(ir))
            {
                string pdb = Path.ChangeExtension(outputPath, ".pdb");
                ValidateOutput(pdb);
                if (pathComparer.Equals(pdb, dependencyFile) || pathComparer.Equals(pdb, dependencyFile == null ? null : dependencyFile + ".state"))
                    throw new ArgumentException("PDB and dependency outputs must use different paths.");
                if (included.Contains(pdb)) throw new ArgumentException("PDB output must differ from included source files.");
            }
            if (OperatingSystem.IsWindows() && !irOnly && outputType == "DynamicLibrary")
            {
                foreach (string suffix in new[] { ".lib", ".exp" })
                {
                    string companion = Path.ChangeExtension(outputPath, suffix);
                    ValidateOutput(companion);
                    if (pathComparer.Equals(companion, outputPath) || included.Contains(companion) || pathComparer.Equals(companion, dependencyFile) ||
                        pathComparer.Equals(companion, dependencyFile == null ? null : dependencyFile + ".state"))
                        throw new ArgumentException("Library companion outputs must differ from the library, included files, and dependency outputs.");
                }
            }
            WriteDependencies();
            string[] interfaceOutputs = outputType == "Executable" ? [] : [outputPath + ".gfi", outputPath + ".gfi.gfh", outputPath + ".hpp", outputPath + ".cmake"];
            foreach (string companion in interfaceOutputs)
            {
                ValidateOutput(companion);
                if (included.Contains(companion) || pathComparer.Equals(companion, dependencyFile) || pathComparer.Equals(companion, dependencyFile == null ? null : dependencyFile + ".state"))
                    throw new ArgumentException("Library interface output must differ from included files and dependency outputs.");
            }
            var deployments = runtimeFiles.Select(file => (Source: file, Destination: Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileName(file))))
                .Where(pair => !pathComparer.Equals(pair.Source, pair.Destination)).ToArray();
            foreach (var deployment in deployments)
            {
                ValidateOutput(deployment.Destination);
                if (pathComparer.Equals(deployment.Destination, outputPath) || interfaceOutputs.Contains(deployment.Destination, pathComparer) ||
                    included.Contains(deployment.Destination) || pathComparer.Equals(deployment.Destination, dependencyFile) ||
                    pathComparer.Equals(deployment.Destination, dependencyFile == null ? null : dependencyFile + ".state"))
                    throw new ArgumentException("Runtime library deployment conflicts with a build output or included source file.");
            }
            if (irOnly)
            {
                string manifest = outputPath + ".foreign.json";
                ValidateOutput(manifest);
                if (included.Contains(manifest)) throw new ArgumentException("Foreign manifest output must differ from included source files.");
                if (program.ForeignUnits.Count > 0)
                    File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(program.ForeignUnits));
                else if (File.Exists(manifest)) File.Delete(manifest);
                File.WriteAllText(outputPath, ir);
                return 0;
            }
            string temporaryIr = Path.Combine(Path.GetTempPath(), "gflat_" + Guid.NewGuid().ToString("N") + ".ll");
            try
            {
                File.WriteAllText(temporaryIr, ir);
                ProcessResult compiled = outputType == "StaticLibrary"
                    ? NativeToolchain.CompileStaticLibrary(temporaryIr, outputPath, clang,
                        optimizationLevel ?? OptimizationLevel.Parse(Environment.GetEnvironmentVariable("GFLAT_OPT_LEVEL") ?? "0"),
                        program.ForeignUnits, exceptionReserveBytes ?? ExceptionReserve.DefaultBytes, runtimeLibrary)
                    : NativeToolchain.Compile(temporaryIr, outputPath, clang, exceptionReserveBytes ?? ExceptionReserve.DefaultBytes, optimizationLevel, nativeLinkInputs, program.ForeignUnits, sharedLibrary: outputType == "DynamicLibrary", runtimeLibrary: runtimeLibrary);
                if (compiled.ExitCode != 0) { Console.Error.WriteLine("Native toolchain error: " + compiled.StandardError); return 2; }
                if (program.Interface is { } libraryInterface)
                {
                    var manifest = libraryInterface.ToXml();
                    manifest.SetAttributeValue("Library", Path.GetFileName(outputPath));
                    manifest.SetAttributeValue("Kind", outputType);
                    manifest.SetAttributeValue("RuntimeLibrary", runtimeLibrary);
                    manifest.Add(new System.Xml.Linq.XElement("NativeLibraries", nativeLinkInputs.Libraries.Select(library =>
                        new System.Xml.Linq.XElement("Library", new System.Xml.Linq.XAttribute("Kind", library.Kind),
                            new System.Xml.Linq.XAttribute("Value", library.Kind == "File" ? Path.GetRelativePath(Path.GetDirectoryName(outputPath)!, library.Value) : library.Value)))));
                    manifest.Add(new System.Xml.Linq.XElement("NativeDirectories", nativeLinkInputs.Directories.Select(directory =>
                        new System.Xml.Linq.XElement("Directory", Path.GetRelativePath(Path.GetDirectoryName(outputPath)!, directory)))));
                    manifest.SetAttributeValue("CppRuntime", NativeToolchain.RequiredNativeRuntimes(ir).Exceptions || program.ForeignUnits.Any(unit => unit.Language == "cpp"));
                    manifest.Add(new System.Xml.Linq.XElement("RuntimeFiles", runtimeFiles.Select(file => new System.Xml.Linq.XElement("File",
                        Path.GetRelativePath(Path.GetDirectoryName(outputPath)!, file)))));
                    File.WriteAllText(interfaceOutputs[0], manifest.ToString());
                    File.WriteAllText(interfaceOutputs[1], libraryInterface.HeaderText);
                    File.WriteAllText(interfaceOutputs[2], libraryInterface.CppHeader());
                    File.WriteAllText(interfaceOutputs[3], NativeCmakeExport.Generate(outputPath, outputType == "DynamicLibrary"));
                }
                foreach (var deployment in deployments) File.Copy(deployment.Source, deployment.Destination, overwrite: true);
                if (!run) return 0;
                return NativeToolchain.RunProgram(outputPath, programArguments);
            }
            finally
            {
                try { File.Delete(temporaryIr); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // Cleanup must not replace a native diagnostic or the program's exit code.
                    Console.Error.WriteLine($"Warning: could not delete temporary IR '{temporaryIr}': {cleanup.Message}");
                }
            }
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
