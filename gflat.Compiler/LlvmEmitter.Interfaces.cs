using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    // Interface views formed from Base* must still dispatch through the object's
    // vptr: the compile-time source type need not be the object's actual type.
    private string EmitInterfaceVirtualThunk(TypeChecker.ClassInfo owner, string iface,
        MethodDeclaration method, int slot, string returnType, List<string> parameterTypes)
    {
        string name = $"gflat${owner.Name}${iface}${method.Name}$interface_thunk";
        var parameters = parameterTypes.Select((t, i) => $"{t} %arg{i}").ToList();
        string signature = $"{returnType} ({string.Join(", ", parameterTypes)})*";
        EmitGlobal($"define internal {returnType} @{name}({string.Join(", ", parameters)}) {{");
        EmitGlobal("entry:");
        EmitGlobal($"    %object = bitcast {parameterTypes[0]} %arg0 to %{owner.Name}*");
        EmitGlobal($"    %vptr = getelementptr %{owner.Name}, %{owner.Name}* %object, i32 0, i32 0");
        EmitGlobal("    %table = load i8**, i8*** %vptr");
        EmitGlobal($"    %slot = getelementptr i8*, i8** %table, i32 {slot}");
        EmitGlobal("    %raw = load i8*, i8** %slot");
        EmitGlobal($"    %fn = bitcast i8* %raw to {signature}");
        string call = $"call {returnType} %fn({string.Join(", ", parameters)})";
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
