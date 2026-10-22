namespace Hashing;

// A compile-time contract; no instance or function-pointer table is needed.
public interface IByteHash
{
    static uint Seed { get; }
    static uint Append(uint state, byte value);
}

public static class Checksums<Algorithm : IByteHash>
{
    public struct Result
    {
        public uint Value;
        public nuint Length;
    }

    public static Result Compute(immutable(char*) data, nuint length)
    {
        Result result = new Result();
        result.Value = Algorithm::Seed;
        result.Length = length;
        for (nuint i = 0; i < length; i++)
            result.Value = Algorithm::Append(result.Value, (byte)data[i]);
        return result;
    }
}
