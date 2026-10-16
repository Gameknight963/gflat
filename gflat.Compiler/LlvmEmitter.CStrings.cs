using gflat.comptime;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Dictionary<string, (string Global, int Length)> _cstringStorage = new();

    // Pool by emitted bytes, not source escape spelling. Constant evaluation
    // uses the same storage identity when comparing immutable literal pointers.
    private string CStringAddress(ConstValue.String value)
    {
        byte[] bytes = StringLiteralEncoding.Bytes(value.Value);
        string key = Convert.ToBase64String(bytes);
        if (!_cstringStorage.TryGetValue(key, out var storage))
        {
            storage = (NewGlobal(), bytes.Length + 1);
            _cstringStorage[key] = storage;
            EmitGlobal($"{storage.Global} = private constant [{storage.Length} x i8] c\"{StringLiteralEncoding.LlvmBytes(bytes)}\\00\"");
        }
        return $"getelementptr inbounds ([{storage.Length} x i8], [{storage.Length} x i8]* {storage.Global}, i32 0, i64 {value.Offset})";
    }
}
