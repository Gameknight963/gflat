using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private sealed class InterpolationTextCleanup(string pointer) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter) => emitter.Emit($"    call void @gflat$Allocator$Free(i8* {pointer})");
    }

    private void EmitInterpolation(InterpolatedStringExpression node)
    {
        var plan = _typeChecker.GetInterpolation(node);
        var bindings = new List<string>();
        string Bind(IdentifierExpression id, string value)
        {
            string type = EmitType(_typeChecker.GetType(id));
            string slot = NewTemp();
            Emit($"    {slot} = alloca {type}");
            Emit($"    store {type} {value}, {type}* {slot}");
            _locals[id.Name] = slot;
            bindings.Add(id.Name);
            return slot;
        }
        _deferScopes.Add(new());
        try
        {
            plan.Factory.Accept(this);
            string result = Bind(plan.Result, Pop());
            TypeExpression resultType = _typeChecker.GetType(node);
            // Keep the partial destination alive for every exceptional edge. On
            // success the returned value transfers ownership to the surrounding expression.
            if (_typeChecker.HasDestructor(resultType))
                _deferScopes[^1].Add(new TemporaryCleanup(resultType, result));
            foreach (var part in plan.Parts)
            {
                _deferScopes.Add(new());
                try
                {
                    if (part.Convert != null)
                    {
                        part.Convert.Accept(this);
                        string text = Pop();
                        _deferScopes[^1].Add(new InterpolationTextCleanup(text));
                        Bind(part.Text!, text);
                        Bind(part.Length!, EmitCStringLength(text));
                    }
                    part.Append.Accept(this);
                    EmitDefersDownTo(_deferScopes.Count - 1);
                }
                finally { _deferScopes.RemoveAt(_deferScopes.Count - 1); }
            }
            string value = NewTemp(), llvmType = EmitType(resultType);
            Emit($"    {value} = load {llvmType}, {llvmType}* {result}");
            Push(value);
        }
        finally
        {
            _deferScopes.RemoveAt(_deferScopes.Count - 1);
            foreach (string name in bindings) _locals.Remove(name);
        }
    }

    private string EmitCStringLength(string text)
    {
        string sizeType = $"i{TargetInfo.Default.PointerBits}";
        string slot = NewTemp(), test = NewLabel("text_length"), advance = NewLabel("text_next"), done = NewLabel("text_end");
        Emit($"    {slot} = alloca {sizeType}");
        Emit($"    store {sizeType} 0, {sizeType}* {slot}");
        Emit($"    br label %{test}");
        Emit($"{test}:");
        string length = NewTemp(), address = NewTemp(), value = NewTemp(), zero = NewTemp();
        Emit($"    {length} = load {sizeType}, {sizeType}* {slot}");
        Emit($"    {address} = getelementptr i8, i8* {text}, {sizeType} {length}");
        Emit($"    {value} = load i8, i8* {address}");
        Emit($"    {zero} = icmp eq i8 {value}, 0");
        Emit($"    br i1 {zero}, label %{done}, label %{advance}");
        Emit($"{advance}:");
        string next = NewTemp();
        Emit($"    {next} = add {sizeType} {length}, 1");
        Emit($"    store {sizeType} {next}, {sizeType}* {slot}");
        Emit($"    br label %{test}");
        Emit($"{done}:");
        return length;
    }
}
