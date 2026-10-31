using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    // A materialized class value owns only its static type's storage. Borrowed
    // reads keep their original header and never pass through this boundary.
    private string EmitOwnedClassHeader(TypeExpression type, string value)
    {
        if (_typeChecker.ResolveAlias(type) is not NamedTypeExpression named ||
            _typeChecker.GetClassByIdentity(named.Name) is not { } cls) return value;
        string result = NewTemp();
        Emit($"    {result} = insertvalue %{cls.Name} {value}, i8** bitcast ([{cls.VirtualMethods.Count} x i8*]* @{cls.Name}$vtable to i8**), 0");
        return result;
    }

    private void EmitConstructorCall(string name, ConstructorDeclaration? constructor, string arguments)
    {
        EmitCallInstruction("void", "@" + name, arguments, handleUnwind: constructor?.IsNoThrow != true);
    }

    private void EmitCopyValue(AstNode expression, TypeExpression type)
    {
        if (expression is AssignmentExpression)
        {
            // The assignment has committed its owner. Stage its result only as
            // a borrowed copy source, never as another destructor-owning value.
            expression.Accept(this);
            string borrowed = EmitOwnedClassHeader(type, Pop());
            if (_typeChecker.UsesCollector && HasManagedReferences(type))
                Push(CreateManagedTemporary(type, borrowed));
            else
            {
                string scratch = NewTemp(), storageType = EmitType(type);
                EmitAlloca(storageType, scratch);
                Emit($"    store {storageType} {borrowed}, {storageType}* {scratch}");
                Push(scratch);
            }
        }
        else EmitAddress(expression);
        string source = Pop(), destination = NewTemp(), llvm = EmitType(type);
        EmitAlloca(llvm, destination);
        EmitManagedReferences(type, destination, true, initialize: true);
        RegisterManagedCleanup(type, destination);
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
            value = EmitOwnedClassHeader(type, value);
            EmitManagedStore(type, value, destination);
            return;
        }
        if (_typeChecker.CopyConstructor(type) is ConstructorDeclaration constructor)
        {
            string name = ((NamedTypeExpression)type).Name;
            string ns = _typeChecker.GetStructByIdentity(name)?.Namespace ?? _typeChecker.GetClassByIdentity(name)!.Namespace;
            EmitManagedStore(type, GetDefaultValue(type), destination);
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
                EmitAlloca("i32", index);
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
            var cls = _typeChecker.GetClassByIdentity(name);
            var str = _typeChecker.GetStructByIdentity(name);
            EmitManagedStore(type, GetDefaultValue(type), destination);
            if (cls?.BaseClass is string parent)
            {
                string src = NewTemp(), dst = NewTemp();
                Emit($"    {src} = bitcast {llvm}* {source} to %{parent}*");
                Emit($"    {dst} = bitcast {llvm}* {destination} to %{parent}*");
                var baseType = NamedTypeExpression.Resolved(parent, type.Line);
                EmitCopyInto(baseType, src, dst);
                if (_typeChecker.HasDestructor(baseType)) _deferScopes[^1].Add(new BaseCopyCleanup(parent, dst));
            }
            var fields = str?.FieldDeclarations ?? cls!.FieldDeclarations;
            foreach (var field in fields.Where(f => !f.IsStatic))
            {
                int position = str != null ? str.FieldIndex(field.Name) : cls!.FieldIndex(field.Name) + 1;
                string src = NewTemp(), dst = NewTemp();
                Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 {position}");
                Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 {position}");
                EmitCopyInto(field.Type, src, dst);
                if (_typeChecker.HasDestructor(field.Type)) _deferScopes[^1].Add(new PartialValueCleanup(field.Type, dst));
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
            string ns = emitter._typeChecker.GetClassByIdentity(name)!.Namespace;
            string destructor = ns.Length > 0 ? $"gflat${ns}${name}$dtor" : $"gflat${name}$dtor";
            emitter.EmitCallInstruction("void", "@" + destructor, $"%{name}* {address}", handleUnwind: false);
            emitter.EmitManagedReferences(NamedTypeExpression.Resolved(name, 0), address, true);
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
            new PartialValueCleanup(type.ElementType, ptr).Execute(e);
            e.Emit($"    br label %{test}"); e.Emit($"{done}:");
        }
    }
}
