using gflat.CompileExceptions;
using gflat.diagnostics;

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
            bool run = false, irOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {args[i - 1]}");
                switch (args[i])
                {
                    case "--help": case "-h":
                        Console.WriteLine("gflat <source.gf> [more.gf ...] [-o output] [--emit-ir] [--run] [--clang path] [--target x86_64-pc-windows-msvc]");
                        return 0;
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
            if (sourcePaths.Count == 0) throw new ArgumentException("Specify a source file. Use --help for usage.");
            if (run && irOnly) throw new ArgumentException("--run cannot be combined with --emit-ir.");
            outputPath = Path.GetFullPath(outputPath ?? Path.ChangeExtension(sourcePaths[0], irOnly ? ".ll" : ".exe"));
            var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (sourcePaths.Distinct(pathComparer).Count() != sourcePaths.Count)
                throw new ArgumentException("A source file may only be supplied once.");
            if (sourcePaths.Contains(outputPath, pathComparer))
                throw new ArgumentException("Output must differ from every source file.");
            var sources = sourcePaths.Select(path => new SourceFile(path, File.ReadAllText(path))).ToArray();
            string ir = Compiler.Emit(sources, diagnostics);
            if (diagnostics.Count > 0) Console.Error.WriteLine(diagnostics.FormatAll());
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
