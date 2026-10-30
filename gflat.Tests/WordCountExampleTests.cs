namespace gflat.Tests;

public class WordCountExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualExampleCountsAcrossChunksAndReleasesTheInputFile(int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Examples/word-count.gf");
        string directory = Path.Combine(Path.GetTempPath(), "gflat_words_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input=Path.Combine(directory,"input.txt");
            File.WriteAllText(input, "Apple apple! PEAR\0pear\n" + new string(' ',4070) + "Banana BANANA final");
            var sources=Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Standard"),"*.gf",SearchOption.AllDirectories)
                .Select(file=>new SourceFile(file,File.ReadAllText(file)))
                .Append(new SourceFile(path,File.ReadAllText(path)));
            string ir=Path.Combine(directory,"words.ll"), exe=Path.Combine(directory,"words.exe");
            File.WriteAllText(ir,Compiler.Emit(sources,options:new(){InternalizeFunctions=true}));
            var compiled=NativeToolchain.Compile(ir,exe,optimizationLevel:optimization);
            Assert.True(compiled.ExitCode==0,compiled.StandardError);
            var run=NativeToolchain.Run(exe,[input],30000);
            Assert.Equal(0,run.ExitCode);
            Assert.Equal("",run.StandardError);
            Assert.Equal(new[]{"apple: 2","banana: 2","final: 1","pear: 2"},
                run.StandardOutput.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Order().ToArray());
            File.Delete(input);
            var missing=NativeToolchain.Run(exe,[input],30000);
            Assert.Equal(1,missing.ExitCode);
            Assert.Equal("",missing.StandardOutput);
            Assert.Equal("Unable to read input file.",missing.StandardError.TrimEnd('\r','\n'));
            var usage=NativeToolchain.Run(exe,[],30000);
            Assert.Equal(2,usage.ExitCode);
            Assert.Equal("",usage.StandardOutput);
            Assert.Equal("Usage: word-count <file>",usage.StandardError.TrimEnd('\r','\n'));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReported("word-count", optimization, "Unable to write word counts.");
}
