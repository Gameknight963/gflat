using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
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
                    if (part.Conversion != null)
                    {
                        part.Conversion.Accept(this);
                        string converted = Bind(part.Converted!, Pop());
                        TypeExpression convertedType = _typeChecker.GetType(part.Converted!);
                        if (_typeChecker.HasDestructor(convertedType))
                            _deferScopes[^1].Add(new TemporaryCleanup(convertedType, converted));
                    }
                    part.Action.Accept(this);
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

}
