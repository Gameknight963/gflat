using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExampleOutputFailureTests
{
    [Fact]
    public async Task CompilerFailurePreservesCombinedSourceAndOriginalDiagnosticWithoutNativeCompilation()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        string directory=Path.Combine(Path.GetTempPath(),"gflat-output-failure-regression-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string stem=Path.Combine(directory,"broken-example");
        string? retained=null;
        File.WriteAllText(stem+".gf","""
            int main(int argc, char** argv) { return unknownOutputFailureSnapshotSymbol; }
            """);
        try
        {
            var failure=Assert.Throws<TypeCheckException>(()=>ExampleOutputFailure.AssertReportedWithArguments(
                stem,0,"unused buffered diagnostic","unused immediate diagnostic"));
            Assert.Contains("unknownOutputFailureSnapshotSymbol",failure.Message,StringComparison.Ordinal);
            retained=Assert.IsType<string>(failure.Data["ExampleOutputFailureDirectory"]);
            Assert.True(Directory.Exists(retained));
            string snapshot=Path.Combine(retained,"example-with-output-failure.gf");
            Assert.True(File.Exists(snapshot));
            string combined=File.ReadAllText(snapshot);
            Assert.Contains("int ExampleMain(int argc, char** argv)",combined,StringComparison.Ordinal);
            Assert.Contains("return unknownOutputFailureSnapshotSymbol;",combined,StringComparison.Ordinal);
            Assert.Contains("int main(int argc,char** argv)",combined,StringComparison.Ordinal);
            Assert.Contains("bool BreakOutput(void* stream)",combined,StringComparison.Ordinal);
            Assert.Contains(snapshot,failure.Message,StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(retained,"main.ll")));
            Assert.False(File.Exists(Path.Combine(retained,"tool.exe")));
        }
        catch(Exception error)
        {
            error.Data["ExampleOutputFailureRegressionDirectory"]=directory;
            if(retained!=null)error.Data["PreservedHelperDirectory"]=retained;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(retained!);
        await TestDirectoryCleanup.DeleteAsync(directory);
    }
}
