namespace gflat.comptime;

public abstract partial record ConstValue
{
    internal sealed record Uninitialized : ConstValue
    {
        public override string ToString() => "uninitialized storage";
    }
}
