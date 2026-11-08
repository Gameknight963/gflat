namespace gflat;

// Pointer-only native storage. Construction and destruction stay in native code.
internal sealed record NativeRecordLayout(int Size, int Alignment);
