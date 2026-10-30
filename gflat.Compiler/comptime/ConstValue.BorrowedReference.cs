namespace gflat.comptime;

public abstract partial record ConstValue
{
    // An interpreter address, not a relocatable address for emitted data. The
    // captured cell keeps scalar loads/stores tied to their original storage.
    internal sealed record BorrowedReference(Func<ConstValue> Read, Action<ConstValue> Write) : ConstValue;
}
