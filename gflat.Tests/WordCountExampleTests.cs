namespace gflat.Tests;

public class WordCountExampleTests
{
    [Fact]
    public void ActualExampleCountsAcrossChunksAndReleasesTheInputFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Examples/word-count.gf");
        string directory = Path.Combine(Path.GetTempPath(), "gflat_words_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"input.txt");
            File.WriteAllText(input, "Apple apple! PEAR\0pear\n" + new string(' ',4070) + "Banana BANANA final");
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Std"),"*.gf",SearchOption.AllDirectories)
                .Select(file=>new SourceFile(file,File.ReadAllText(file)))
                .Append(new SourceFile(path,File.ReadAllText(path)));
            string ir=Path.Combine(directory,"words.ll"), exe=Path.Combine(directory,"words.exe");
            File.WriteAllText(ir,Compiler.Emit(sources));
            var compiled=NativeToolchain.Compile(ir,exe);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var run=NativeToolchain.Run(exe,[input],30000);
            Assert.Equal(0,run.ExitCode);
            Assert.Equal(new[]{"apple: 2","banana: 2","final: 1","pear: 2"},
                run.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Order().ToArray());
            File.Delete(input);
            var missing=NativeToolchain.Run(exe,[input],30000);
            Assert.Equal(1,missing.ExitCode);
            Assert.Contains("Unable to read input",missing.StandardOutput);
            Assert.Equal(2,NativeToolchain.Run(exe,[],30000).ExitCode);
        }
        finally { Directory.Delete(directory,true); }
    }
}
