using gflat.ast;

namespace gflat;

internal sealed class ForeignImportState
{
    internal Dictionary<string, string> Symbols { get; } = new();
    internal Dictionary<string, string> RecordSignatures { get; } = new();
    internal Dictionary<string, StructDeclaration> Records { get; } = new();
}
