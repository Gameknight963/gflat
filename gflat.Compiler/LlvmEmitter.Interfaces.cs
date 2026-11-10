using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void EmitPrimitiveInterfaceTables()
    {
        foreach (var implementation in _typeChecker.GetPrimitiveImplementations())
        {
            var entries = new List<string>();
            foreach (var contract in implementation.Interface.Methods)
            {
                var method = implementation.Methods[contract.Name];
                string ns = _typeChecker.GetFunctionNamespace(method);
                string name = (ns.Length == 0 ? "gflat$" : "gflat$" + ns + "$") + method.Name + _typeChecker.GetOverloadSuffix(method);
                var parameters = method.Parameters.Select(p => EmitParamType(p.Type)).ToList();
                AddManagedResultParameter(method.ReturnType, parameters);
                entries.Add($"i8* bitcast ({EmitFunctionReturnType(method.ReturnType)} ({string.Join(", ", parameters)})* @{name} to i8*)");
            }
            entries.Add(EmitInterfaceDestructorEntry(implementation.Primitive));
            AppendRequiredInterfaceTables(implementation.Primitive, implementation.Interface, entries);
            EmitGlobal($"@{implementation.Primitive}${implementation.Interface.Name}$vtable = internal constant [{entries.Count} x i8*] [ {string.Join(", ", entries)} ]");
        }
    }

    private readonly HashSet<string> _interfaceDestructors = new();

    private static int InterfaceTableSize(TypeChecker.InterfaceInfo contract)
        => contract.Methods.Count + 1 + contract.RequiredInterfaces.Count;

    private void AppendRequiredInterfaceTables(string owner, TypeChecker.InterfaceInfo contract, List<string> entries)
    {
        foreach (string required in contract.RequiredInterfaces)
            entries.Add($"i8* bitcast ([{InterfaceTableSize(_typeChecker.GetInterface(required)!)} x i8*]* @{owner}${required}$vtable to i8*)");
    }

    private string EmitRequiredInterfaceConversion(string value, string source, string target)
    {
        var contract = _typeChecker.GetInterface(source)!;
        int index = contract.RequiredInterfaces.IndexOf(target);
        if (index < 0) throw new InvalidOperationException("Missing required interface conversion");
        string table = NewTemp(), present = NewTemp();
        string convert = NewLabel("interface_convert"), empty = NewLabel("interface_empty"), ready = NewLabel("interface_ready");
        Emit($"    {table} = extractvalue {{ i8*, i8** }} {value}, 1");
        Emit($"    {present} = icmp ne i8** {table}, null");
        Emit($"    br i1 {present}, label %{convert}, label %{empty}");
        Emit(convert + ":");
        string slot = NewTemp(), raw = NewTemp(), required = NewTemp(), converted = NewTemp();
        Emit($"    {slot} = getelementptr i8*, i8** {table}, i32 {contract.Methods.Count + 1 + index}");
        Emit($"    {raw} = load i8*, i8** {slot}");
        Emit($"    {required} = bitcast i8* {raw} to i8**");
        Emit($"    {converted} = insertvalue {{ i8*, i8** }} {value}, i8** {required}, 1");
        Emit($"    br label %{ready}");
        Emit(empty + ":");
        Emit($"    br label %{ready}");
        Emit(ready + ":");
        string result = NewTemp();
        Emit($"    {result} = phi {{ i8*, i8** }} [ {converted}, %{convert} ], [ zeroinitializer, %{empty} ]");
        return result;
    }

    private string EmitInterfaceDestructorEntry(string owner)
    {
        string thunk = $"gflat${owner}$interface_destroy";
        if (_interfaceDestructors.Add(owner))
        {
            EmitGlobal($"define internal void @{thunk}(i8* %data) nounwind{NoBuiltinsAttribute} {{");
            EmitGlobal("entry:");
            if (_typeChecker.IsClassByIdentity(owner) || NeedsCleanup(NamedTypeExpression.Resolved(owner, 0)))
            {
                var cls = _typeChecker.GetClassByIdentity(owner);
                string ns = cls?.Namespace ?? _typeChecker.GetStructByIdentity(owner)!.Namespace;
                string name = ns.Length > 0 ? $"gflat${ns}${owner}$dtor" : $"gflat${owner}$dtor";
                EmitGlobal($"    %object = bitcast i8* %data to %{owner}*");
                if (cls is { DestructorSlot: >= 0 })
                {
                    EmitGlobal($"    %vptr = getelementptr %{owner}, %{owner}* %object, i32 0, i32 0");
                    EmitGlobal("    %table = load i8**, i8*** %vptr");
                    EmitGlobal($"    %slot = getelementptr i8*, i8** %table, i32 {cls.DestructorSlot}");
                    EmitGlobal("    %raw = load i8*, i8** %slot");
                    EmitGlobal($"    %fn = bitcast i8* %raw to void (%{owner}*)*");
                    EmitGlobal($"    call void %fn(%{owner}* %object)");
                }
                else EmitGlobal($"    call void @{name}(%{owner}* %object)");
            }
            EmitGlobal("    ret void");
            EmitGlobal("}");
        }
        return $"i8* bitcast (void (i8*)* @{thunk} to i8*)";
    }

    private string EmitInterfaceDestruction(string value, string iface)
    {
        string data = NewTemp(), table = NewTemp(), slot = NewTemp(), raw = NewTemp(), fn = NewTemp();
        Emit($"    {data} = extractvalue {{ i8*, i8** }} {value}, 0");
        Emit($"    {table} = extractvalue {{ i8*, i8** }} {value}, 1");
        Emit($"    {slot} = getelementptr i8*, i8** {table}, i32 {_typeChecker.GetInterface(iface)!.Methods.Count}");
        Emit($"    {raw} = load i8*, i8** {slot}");
        Emit($"    {fn} = bitcast i8* {raw} to void (i8*)*");
        EmitCallInstruction("void", fn, $"i8* {data}", handleUnwind: false);
        return data;
    }

    // Interface views formed from Base* must still dispatch through the object's
    // vptr: the compile-time source type need not be the object's actual type.
    private string EmitInterfaceVirtualThunk(TypeChecker.ClassInfo owner, string iface,
        MethodDeclaration method, int slot, string returnType, List<string> parameterTypes)
    {
        string name = $"gflat${owner.Name}${iface}${method.Name}$interface_thunk";
        var parameters = parameterTypes.Select((t, i) =>
            $"{(i > 0 && i <= method.Parameters.Count ? EmitAbiParamType(method.Parameters[i - 1].Type) : t)} %arg{i}").ToList();
        string signature = $"{returnType} ({string.Join(", ", parameterTypes)})*";
        string abiReturn = EmitAbiFunctionReturnType(method.ReturnType);
        EmitGlobal($"define internal {abiReturn} @{name}({string.Join(", ", parameters)}){NoBuiltinsAttribute} {{");
        EmitGlobal("entry:");
        EmitGlobal($"    %object = bitcast {parameterTypes[0]} %arg0 to %{owner.Name}*");
        EmitGlobal($"    %vptr = getelementptr %{owner.Name}, %{owner.Name}* %object, i32 0, i32 0");
        EmitGlobal("    %table = load i8**, i8*** %vptr");
        EmitGlobal($"    %slot = getelementptr i8*, i8** %table, i32 {slot}");
        EmitGlobal("    %raw = load i8*, i8** %slot");
        EmitGlobal($"    %fn = bitcast i8* %raw to {signature}");
        string call = $"call {abiReturn} %fn({string.Join(", ", parameters)})";
        if (returnType == "void")
        {
            EmitGlobal("    " + call);
            EmitGlobal("    ret void");
        }
        else
        {
            EmitGlobal("    %result = " + call);
            EmitGlobal($"    ret {returnType} %result");
        }
        EmitGlobal("}");
        return name;
    }
}
