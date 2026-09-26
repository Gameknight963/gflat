namespace gflat.Tests;

public class TargetInfoTests
{
    [Fact]
    public void EmittedModuleUsesTheHostTargetAndLayout()
    {
        string triple = OperatingSystem.IsLinux() ? "x86_64-unknown-linux-gnu" : "x86_64-pc-windows-msvc";
        string mangling = OperatingSystem.IsLinux() ? "e-m:e-" : "e-m:w-";
        Assert.Equal(triple, TargetInfo.Default.Triple);
        Assert.StartsWith(mangling, TargetInfo.Default.DataLayout);
        Assert.Equal(8, TargetInfo.Default.PointerBytes);
        string ir = Compiler.Emit("int main() => 0;");
        Assert.Contains($"target triple = \"{triple}\"", ir);
        Assert.Contains($"target datalayout = \"{TargetInfo.Default.DataLayout}\"", ir);
        Assert.Same(TargetInfo.Default, TargetInfo.Parse(triple));
    }

    [Fact]
    public void ForeignTargetIsRejectedInsteadOfSilentlyUsingHostAbi()
    {
        string foreign = OperatingSystem.IsLinux() ? "x86_64-pc-windows-msvc" : "x86_64-unknown-linux-gnu";
        Assert.Throws<ArgumentException>(() => TargetInfo.Parse(foreign));
    }
}
