namespace gflat;

// Native storage and the lifetime operations available to the host compiler.
// Missing size and alignment denote a forward declaration.
internal sealed record NativeRecordLayout(int? Size, int? Alignment, NativeValueTraits? Values = null);

internal sealed record NativeValueTraits(bool CanCopy, bool CopyNoThrow, bool MoveNoThrow, bool DestroyNoThrow,
    bool CanDefault, bool DefaultNoThrow, bool CanAllocateDefault)
{
    public bool SupportsValues => MoveNoThrow && DestroyNoThrow;
}
