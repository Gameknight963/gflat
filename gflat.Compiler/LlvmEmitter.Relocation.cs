using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool BindRelocatedParameter(Parameter parameter)
    {
        if (!_typeChecker.RequiresRelocation(parameter.Type)) return false;
        string address = NewTemp();
        EmitAlloca(EmitType(parameter.Type), address);
        EmitManagedStore(parameter.Type, GetDefaultValue(parameter.Type), address, initialize: true);
        EmitRelocateInto(parameter.Type, EmitParameterName(parameter.Name), address);
        _locals[parameter.Name] = address;
        RegisterManagedParameter(parameter, address, register: false);
        EmitDebugVariable(parameter.Name, parameter.Type, address, parameter);
        if (_typeChecker.HasDestructor(parameter.Type))
            _parameterOwners.Add(RegisterOwner(parameter.Type, address, true));
        return true;
    }

    private void RememberOwnedLocation(TypeExpression type, string value, string address)
    {
        if (_typeChecker.RequiresRelocation(type)) CurrentFunction.OwnedValueLocations[value] = address;
    }

    private bool RelocateOwnedValue(TypeExpression type, string value, string destination)
    {
        if (!_typeChecker.RequiresRelocation(type) || !CurrentFunction.OwnedValueLocations.TryGetValue(value, out string? source)) return false;
        if (source != destination)
        {
            string llvm = EmitType(type);
            Emit($"    store {llvm} {GetDefaultValue(type)}, {llvm}* {destination}");
            EmitRelocateInto(type, source, destination);
            EmitManagedReferences(type, source, false);
            if (CurrentFunction.RelocatedLocals.Remove(source)) RegisterMovedLocalStorage(type, source);
        }
        CurrentFunction.OwnedValueLocations[value] = destination;
        return true;
    }

    private void EmitRelocateInto(TypeExpression type, string source, string destination)
    {
        type = _typeChecker.ResolveAlias(type);
        string llvm = EmitType(type);
        if (!_typeChecker.RequiresRelocation(type))
        {
            string value = NewTemp();
            Emit($"    {value} = load {llvm}, {llvm}* {source}");
            EmitManagedStore(type, value, destination);
            return;
        }
        if (_typeChecker.MoveConstructor(type) is ConstructorDeclaration constructor)
        {
            string name = ((NamedTypeExpression)type).Name;
            string ns = _typeChecker.GetStructByIdentity(name)?.Namespace ?? _typeChecker.GetClassByIdentity(name)!.Namespace;
            EmitManagedStore(type, GetDefaultValue(type), destination);
            EmitConstructorCall(GetConstructorMangledName(name, constructor, ns), constructor,
                $"{llvm}* {destination}, {llvm}* {source}");
            SetOwnerLive(source, false);
            return;
        }
        if (type is ArrayTypeExpression array)
        {
            string index = NewTemp();
            EmitAlloca("i32", index);
            Emit($"    store i32 0, i32* {index}");
            string test = NewLabel("move_test"), body = NewLabel("move_element"), done = NewLabel("move_done");
            Emit($"    br label %{test}"); Emit(test + ":");
            string value = NewTemp(), more = NewTemp();
            Emit($"    {value} = load i32, i32* {index}");
            Emit($"    {more} = icmp ult i32 {value}, {array.Size}");
            Emit($"    br i1 {more}, label %{body}, label %{done}"); Emit(body + ":");
            string src = NewTemp(), dst = NewTemp();
            Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 {value}");
            Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 {value}");
            EmitRelocateInto(array.ElementType, src, dst);
            string next = NewTemp(); Emit($"    {next} = add i32 {value}, 1");
            Emit($"    store i32 {next}, i32* {index}");
            Emit($"    br label %{test}"); Emit(done + ":");
            return;
        }
        var named = (NamedTypeExpression)type;
        var cls = _typeChecker.GetClassByIdentity(named.Name);
        var structure = _typeChecker.GetStructByIdentity(named.Name);
        if (cls?.BaseClass is string parent)
        {
            string src = NewTemp(), dst = NewTemp();
            Emit($"    {src} = bitcast {llvm}* {source} to %{parent}*");
            Emit($"    {dst} = bitcast {llvm}* {destination} to %{parent}*");
            EmitRelocateInto(NamedTypeExpression.Resolved(parent, type.Line), src, dst);
        }
        foreach (var field in (structure?.FieldDeclarations ?? cls!.FieldDeclarations).Where(field => !field.IsStatic))
        {
            int position = structure != null ? structure.FieldIndex(field.Name) : cls!.FieldIndex(field.Name) + 1;
            string src = NewTemp(), dst = NewTemp();
            Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 {position}");
            Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 {position}");
            EmitRelocateInto(field.Type, src, dst);
        }
        if (cls != null)
        {
            string src = NewTemp(), dst = NewTemp(), header = NewTemp();
            Emit($"    {src} = getelementptr {llvm}, {llvm}* {source}, i32 0, i32 0");
            Emit($"    {dst} = getelementptr {llvm}, {llvm}* {destination}, i32 0, i32 0");
            Emit($"    {header} = load i8**, i8*** {src}");
            Emit($"    store i8** {header}, i8*** {dst}");
        }
        SetOwnerLive(source, false);
    }
}
