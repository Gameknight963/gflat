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
            var sourcePaths = new List<string>();
            string? outputPath = null, clang = null;
            bool run = false, irOnly = false, check = false, clean = false;
            string? projectPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
                switch (args[i])
                {
                    case "--help": case "-h":
                        Console.WriteLine("gflat <source.gf> [more.gf ...] [-o output] [--emit-ir] [--run] [--check] [--clang path] [--target x86_64-pc-windows-msvc]\ngflat [build|check|clean] <project.gfproj> [--run] [-o output] [--emit-ir]");
                        return 0;
                    case "build" when i == 0: break;
                    case "check" when i == 0: check = true; break;
                    case "clean" when i == 0: clean = true; break;
                    case "--check": check = true; break;
                    case "-o": outputPath = Value(); break;
                    case "--clang": clang = Value(); break;
                    case "--target": TargetInfo.Parse(Value()); break;
                    case "--run": run = true; break;
                    case "--emit-ir": irOnly = true; break;
                    default:
                        if (args[i].StartsWith('-')) throw new ArgumentException($"Unexpected argument '{args[i]}'");
                        sourcePaths.Add(Path.GetFullPath(args[i])); break;
                }
            }
            ProjectGraph? graph = null;
            if (sourcePaths.Any(p => p.EndsWith(".gfproj", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourcePaths.Count != 1) throw new ArgumentException("Pass one project file, without extra source files.");
                projectPath = sourcePaths[0];
                graph = ProjectGraph.Load(projectPath);
                sourcePaths = graph.Sources.ToList();
                outputPath ??= Path.Combine(graph.Root.DirectoryPath, "bin", graph.Root.Name + (irOnly ? ".ll" : OperatingSystem.IsWindows() ? ".exe" : ""));
                if (graph.Root.Kind == ProjectKind.SourceLibrary)
                {
                    if (run || irOnly || args.Contains("-o")) throw new ArgumentException("SourceLibrary projects have no standalone output; build an executable referencing them.");
                    check = true;
                }
            }
            if (clean)
            {
                if (graph == null || run || irOnly || args.Contains("-o")) throw new ArgumentException("Use clean with a project file and no output options.");
                // Delete only the two known output files, never a directory or an arbitrary path.
                if (graph.Root.Kind == ProjectKind.Executable)
                {
                    File.Delete(outputPath!);
                    File.Delete(Path.Combine(graph.Root.DirectoryPath, "bin", graph.Root.Name + ".ll"));
                }
                return 0;
            }
            if (sourcePaths.Count == 0 && graph?.Root.Kind == ProjectKind.SourceLibrary) return 0;
            if (sourcePaths.Count == 0) throw new ArgumentException("Specify a source file. Use --help for usage.");
            if (check && (run || irOnly)) throw new ArgumentException("--check cannot be combined with --run or --emit-ir.");
            if (run && irOnly) throw new ArgumentException("--run cannot be combined with --emit-ir.");
            outputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(sourcePaths[0], irOnly ? ".ll" : ".exe"));
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (sourcePaths.Distinct(pathComparer).Count() != sourcePaths.Count)
                throw new ArgumentException("A source file may only be supplied once.");
            if (sourcePaths.Contains(outputPath, pathComparer) || graph?.Projects.Any(p => pathComparer.Equals(p.FilePath, outputPath)) == true)
                throw new ArgumentException("Output must differ from every source file.");
            var sources = sourcePaths.Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
            if (check)
            {
                Compiler.Check(sources, diagnostics);
                if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
                return 0;
            }
            string ir = Compiler.Emit(sources, diagnostics);
            if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
            if (projectPath != null) Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (irOnly) { File.WriteAllText(outputPath, ir); return 0; }
            string temporaryIr = Path.Combine(Path.GetTempPath(), "gflat_" + Guid.NewGuid().ToString("N") + ".ll");
            try
            {
                File.WriteAllText(temporaryIr, ir);
                ProcessResult compiled = NativeToolchain.Compile(temporaryIr, outputPath, clang);
                if (compiled.ExitCode != 0) { Console.Error.WriteLine("Native toolchain error: " + compiled.StandardError); return 2; }
                if (!run) return 0;
                ProcessResult execution = NativeToolchain.Run(outputPath, []);
                Console.Write(execution.StandardOutput);
                Console.Error.Write(execution.StandardError);
                return execution.ExitCode;
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
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or TimeoutException)
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
