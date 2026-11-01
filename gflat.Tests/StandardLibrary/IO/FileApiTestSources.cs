namespace gflat.Tests.Std.IO;

internal static class FileApiTestSources
{
    internal static readonly string[] ConvenienceDependencies =
    [
        "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf",
        "core/IComparable.gf", "core/Hashing/primitive_hashing.gf",
        "core/Text/String.gf", "core/Text/ByteSearch.gf", "core/primitive_format.gf",
        "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf",
        "core/Memory/Span.gf", "core/Collections/Vector.gf", "libc/IO/File.gf"
    ];

    internal static IEnumerable<string> WithConveniences(IEnumerable<string> sources)
        => sources.Concat(ConvenienceDependencies).Distinct(StringComparer.Ordinal);
}
