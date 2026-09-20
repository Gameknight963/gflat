namespace gflat;

/// <summary>The only ABI currently supported by the compiler.</summary>
public sealed record TargetInfo(string Triple, int PointerBits, string DataLayout)
{
    public static TargetInfo Default { get; } = new("x86_64-pc-windows-msvc", 64,
        "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128");
    public int PointerBytes => PointerBits / 8;
    public static TargetInfo Parse(string triple) => triple == Default.Triple ? Default :
        throw new ArgumentException($"Unsupported target '{triple}'. Supported target: {Default.Triple}.");
}
