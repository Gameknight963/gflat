using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void EmitConstructorCall(string name, ConstructorDeclaration? constructor, string arguments)
    {
        Emit($"    call void @{name}({arguments})");
    }

    private void EmitCopyValue(AstNode expression, TypeExpression type)
    {
        EmitAddress(expression);
        string source = Pop(), destination = NewTemp(), llvm = EmitType(type);
        Emit($"    {destination} = alloca {llvm}");
        EmitCopyInto(type, source, destination);
        string value = NewTemp();
        Emit($"    {value} = load {llvm}, {llvm}* {destination}");
        Push(value);
    }

    // Build a distinct value, protecting completed subobjects until the whole copy succeeds.
    private void EmitCopyInto(TypeExpression type, string source, string destination)
    {
        type = _typeChecker.ResolveAlias(type);
        string llvm = EmitType(type);
        if (!_typeChecker.RequiresCopy(type))
        {
            string value = NewTemp();
            Emit($"    {value} = load {llvm}, {llvm}* {source}");
            Emit($"    store {llvm} {value}, {llvm}* {destination}");
            return;
        }
        if (_typeChecker.CopyConstructor(type) is ConstructorDeclaration constructor)
        {
            string name = ((NamedTypeExpression)type).Name;
            string ns = _typeChecker.GetStruct(name)?.Namespace ?? _typeChecker.GetClass(name)!.Namespace;
            Emit($"    store {llvm} {GetDefaultValue(type)}, {llvm}* {destination}");
            EmitConstructorCall(GetConstructorMangledName(name, constructor, ns), constructor,
                $"{llvm}* {destination}, {llvm}* {source}");
            return;
        }
        _deferScopes.Add(new());
        try
        {
            if (type is ArrayTypeExpression array)
            {
                // Fixed buffers use a loop so large buffers do not inflate generated code.
                string index = NewTemp();
                Emit($"    {index} = alloca i32");
                Emit($"    store i32 0, i32* {index}");
                _deferScopes[^1].Add(new CopiedArrayCleanup(array, destination, index));
                string test = NewLabel("copy_test"), body = NewLabel("copy_element"), done = NewLabel("copy_done");
                Emit($"    br label %{test}"); Emit($"{test}:");
                string i = NewTemp(), more = NewTemp();
                Emit($"    {i} = load i32, i32* {index}");
                Emit($"    {more} = icmp ult i32 {i}, {array.Size}");
                Emit($"    br i1 {more}, label %{body}, label %{done}"); Emit($"{body}:");
                string src = NewTemp(), dst = NewTemp();
                Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 {i}");
                Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 {i}");
                EmitCopyInto(array.ElementType, src, dst);
                string next = NewTemp(); Emit($"    {next} = add i32 {i}, 1");
                Emit($"    store i32 {next}, i32* {index}"); Emit($"    br label %{test}"); Emit($"{done}:");
                return;
            }
            string name = ((NamedTypeExpression)type).Name;
            var cls = _typeChecker.GetClass(name);
            var str = _typeChecker.GetStruct(name);
            Emit($"    store {llvm} {GetDefaultValue(type)}, {llvm}* {destination}");
            if (cls?.BaseClass is string parent)
            {
                string src = NewTemp(), dst = NewTemp();
                Emit($"    {src} = bitcast {llvm}* {source} to %{parent}*");
                Emit($"    {dst} = bitcast {llvm}* {destination} to %{parent}*");
                var baseType = new NamedTypeExpression(parent, null, type.Line);
                EmitCopyInto(baseType, src, dst);
                if (_typeChecker.HasDestructor(baseType)) _deferScopes[^1].Add(new BaseCopyCleanup(parent, dst));
            }
            var fields = str?.FieldDeclarations ?? cls!.FieldDeclarations;
            foreach (var field in fields)
            {
                int position = str != null ? str.FieldIndex(field.Name) : cls!.FieldIndex(field.Name) + 1;
                string src = NewTemp(), dst = NewTemp();
                Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 {position}");
                Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 {position}");
                EmitCopyInto(field.Type, src, dst);
                if (_typeChecker.HasDestructor(field.Type)) _deferScopes[^1].Add(new TemporaryCleanup(field.Type, dst));
            }
            if (cls != null)
            {
                string slot = NewTemp(), table = NewTemp();
                Emit($"    {slot} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 0");
                Emit($"    {table} = bitcast [{cls.VirtualMethods.Count} x i8*]* @{name}$vtable to i8**");
                Emit($"    store i8** {table}, i8*** {slot}");
            }
        }
        finally { _deferScopes.RemoveAt(_deferScopes.Count - 1); }
    }

    private sealed class BaseCopyCleanup(string name, string address) : IDeferAction
    {
        public void Execute(LlvmEmitter emitter)
        {
            string ns = emitter._typeChecker.GetClass(name)!.Namespace;
            string destructor = ns.Length > 0 ? $"gflat${ns}${name}$dtor" : $"gflat${name}$dtor";
            emitter.Emit($"    call void @{destructor}(%{name}* {address})");
        }
    }

    private sealed class CopiedArrayCleanup(ArrayTypeExpression type, string address, string count) : IDeferAction
    {
        public void Execute(LlvmEmitter e)
        {
            if (!e._typeChecker.HasDestructor(type.ElementType)) return;
            string test = e.NewLabel("copy_cleanup_test"), body = e.NewLabel("copy_cleanup_body"), done = e.NewLabel("copy_cleanup_done");
            e.Emit($"    br label %{test}"); e.Emit($"{test}:");
            string n = e.NewTemp(), more = e.NewTemp();
            e.Emit($"    {n} = load i32, i32* {count}"); e.Emit($"    {more} = icmp ne i32 {n}, 0");
            e.Emit($"    br i1 {more}, label %{body}, label %{done}"); e.Emit($"{body}:");
            string i = e.NewTemp(), ptr = e.NewTemp(), llvm = e.EmitType(type);
            e.Emit($"    {i} = sub i32 {n}, 1"); e.Emit($"    store i32 {i}, i32* {count}");
            e.Emit($"    {ptr} = getelementptr {llvm}, {llvm}* {address}, i32 0, i32 {i}");
            e.EmitDestroy(type.ElementType, ptr); e.Emit($"    br label %{test}"); e.Emit($"{done}:");
        }
    }
}
