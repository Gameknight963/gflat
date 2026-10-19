using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void EmitInterpolation(InterpolatedStringExpression node)
    {
        var plan = _typeChecker.GetInterpolation(node);
        TypeExpression resultType = _typeChecker.GetType(node);
        // Publish the transferred value into the surrounding expression's root
        // storage before releasing the interpolation's own temporary scopes.
        string? resultRoot = _typeChecker.UsesCollector && HasManagedReferences(resultType)
            ? CreateManagedTemporary(resultType) : null;
        var bindings = new List<string>();
        string Bind(IdentifierExpression id, string value)
        {
            TypeExpression type = _typeChecker.GetType(id);
            string slot = NewTemp();
            if (_typeChecker.UsesCollector && HasManagedReferences(type) && !_typeChecker.HasDestructor(type))
            {
                slot = CreateManagedTemporary(type, value);
            }
            else
            {
                EmitAlloca(EmitType(type), slot);
                EmitManagedStore(type, value, slot, initialize: true);
            }
            _locals[id.Name] = slot;
            bindings.Add(id.Name);
            return slot;
        }
        _deferScopes.Add(new());
        try
        {
            plan.Factory.Accept(this);
            string result = Bind(plan.Result, Pop());
            // Keep the partial destination alive for every exceptional edge. On
            // success the returned value transfers ownership to the surrounding expression.
            IDeferAction? resultCleanup = null;
            if (_typeChecker.HasDestructor(resultType))
            {
                resultCleanup = new TemporaryCleanup(resultType, result);
                _deferScopes[^1].Add(resultCleanup);
            }
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
            if (resultRoot != null) EmitManagedStore(resultType, value, resultRoot);
            if (resultCleanup != null)
            {
                _deferScopes[^1].Remove(resultCleanup);
                EmitManagedReferences(resultType, result, false);
            }
            EmitDefersDownTo(_deferScopes.Count - 1);
            Push(value);
        }
        finally
        {
            _deferScopes.RemoveAt(_deferScopes.Count - 1);
            foreach (string name in bindings) _locals.Remove(name);
        }
    }

}
