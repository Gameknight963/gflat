namespace std::Threading;

// Sequentially consistent integer atomics. Locations must remain live and
// initialized, with at least 4-byte alignment for int or 8-byte alignment for long.
// Concurrent overlapping accesses must all use compatible atomic operations.
public static class Interlocked
{
    public static int Read(ref readonly(int) location) nothrow
        => __llvm_module<int>("define i32 @entry(i32* %0) nounwind alwaysinline {\n%value = load atomic i32, i32* %0 seq_cst, align 4\nret i32 %value\n}", "entry", &location);

    public static int Exchange(ref int location, int value) nothrow
        => __llvm_module<int>("define i32 @entry(i32* %0, i32 %1) nounwind alwaysinline {\n%previous = atomicrmw xchg i32* %0, i32 %1 seq_cst, align 4\nret i32 %previous\n}", "entry", &location, value);

    // Strong compare-and-exchange; returns the observed value on either outcome.
    public static int CompareExchange(ref int location, int value, int comparand) nothrow
        => __llvm_module<int>("define i32 @entry(i32* %0, i32 %1, i32 %2) nounwind alwaysinline {\n%pair = cmpxchg i32* %0, i32 %2, i32 %1 seq_cst seq_cst, align 4\n%previous = extractvalue { i32, i1 } %pair, 0\nret i32 %previous\n}", "entry", &location, value, comparand);

    // Returns the updated value, wrapping modulo 2^32 without overflow poison.
    public static int Add(ref int location, int value) nothrow
        => __llvm_module<int>("define i32 @entry(i32* %0, i32 %1) nounwind alwaysinline {\n%previous = atomicrmw add i32* %0, i32 %1 seq_cst, align 4\n%updated = add i32 %previous, %1\nret i32 %updated\n}", "entry", &location, value);

    public static int Increment(ref int location) nothrow
        => Interlocked::Add(ref location, 1);

    public static int Decrement(ref int location) nothrow
        => Interlocked::Add(ref location, -1);

    public static long Read(ref readonly(long) location) nothrow
        => __llvm_module<long>("define i64 @entry(i64* %0) nounwind alwaysinline {\n%value = load atomic i64, i64* %0 seq_cst, align 8\nret i64 %value\n}", "entry", &location);

    public static long Exchange(ref long location, long value) nothrow
        => __llvm_module<long>("define i64 @entry(i64* %0, i64 %1) nounwind alwaysinline {\n%previous = atomicrmw xchg i64* %0, i64 %1 seq_cst, align 8\nret i64 %previous\n}", "entry", &location, value);

    public static long CompareExchange(ref long location, long value, long comparand) nothrow
        => __llvm_module<long>("define i64 @entry(i64* %0, i64 %1, i64 %2) nounwind alwaysinline {\n%pair = cmpxchg i64* %0, i64 %2, i64 %1 seq_cst seq_cst, align 8\n%previous = extractvalue { i64, i1 } %pair, 0\nret i64 %previous\n}", "entry", &location, value, comparand);

    // Returns the updated value, wrapping modulo 2^64 without overflow poison.
    public static long Add(ref long location, long value) nothrow
        => __llvm_module<long>("define i64 @entry(i64* %0, i64 %1) nounwind alwaysinline {\n%previous = atomicrmw add i64* %0, i64 %1 seq_cst, align 8\n%updated = add i64 %previous, %1\nret i64 %updated\n}", "entry", &location, value);

    public static long Increment(ref long location) nothrow
        => Interlocked::Add(ref location, 1l);

    public static long Decrement(ref long location) nothrow
        => Interlocked::Add(ref location, -1l);
}
