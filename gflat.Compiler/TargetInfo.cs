namespace gflat;

/// <summary>Native x64 targets. Cross-compilation is not currently supported.</summary>
public sealed record TargetInfo(string Triple, int PointerBits, string DataLayout)
{
    private static readonly TargetInfo WindowsX64 = new("x86_64-pc-windows-msvc", 64,
        "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128");
    private static readonly TargetInfo LinuxX64 = new("x86_64-unknown-linux-gnu", 64,
        "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128") { ExtendNarrowIntegers = true };
    public static TargetInfo Default { get; } = OperatingSystem.IsLinux() ? LinuxX64 : WindowsX64;
    // System V extends 8/16-bit integer parameters and results; Windows does not.
    // Both supported ABIs zero-extend bool separately.
    public bool ExtendNarrowIntegers { get; init; }
    public string GetIntegerExtensionAttribute(int bits, bool isUnsigned)
    {
        if (bits == 1) return "zeroext";
        if (!ExtendNarrowIntegers || bits is not (8 or 16)) return "";
        return isUnsigned ? "zeroext" : "signext";
    }
    public int PointerBytes => PointerBits / 8;
    public static TargetInfo Parse(string triple) => triple == Default.Triple ? Default :
        throw new ArgumentException($"Unsupported target '{triple}'. Supported target: {Default.Triple}.");
}
