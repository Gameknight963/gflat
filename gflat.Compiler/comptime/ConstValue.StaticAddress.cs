using gflat.ast;

namespace gflat.comptime;

public abstract partial record ConstValue
{
    // A declaration's address is available independently of its initializer.
    // ElementIndex selects an element of array storage after array decay.
    public sealed record StaticAddress(FieldDeclaration Declaration, int? ElementIndex = null) : ConstValue
    {
        public override string ToString() => ElementIndex is { } index
            ? $"&{Declaration.Name}[{index}]" : $"&{Declaration.Name}";
    }
}
