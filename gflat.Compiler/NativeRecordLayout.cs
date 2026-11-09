namespace gflat;

// Pointer-only native storage. Construction and destruction stay in native code.
// Missing size and alignment denote a forward declaration.
internal sealed record NativeRecordLayout(int? Size, int? Alignment);
