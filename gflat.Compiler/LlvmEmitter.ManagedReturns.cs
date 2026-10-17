using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    // The caller keeps result storage alive before control enters the callee.
    // Filling it before callee cleanup avoids a gap with no live reference,
    // which would otherwise reclaim returned objects under reference counting.
    private bool UsesManagedResultStorage(TypeExpression type)
        => _typeChecker.UsesCollector && HasManagedReferences(type);

    private string EmitFunctionReturnType(TypeExpression type)
        => UsesManagedResultStorage(type) ? "void" : EmitType(type);

    private string WithManagedResultParameter(TypeExpression type, string parameters, bool named = false)
    {
        if (!UsesManagedResultStorage(type)) return parameters;
        string result = EmitType(type) + "*" + (named ? " %gc.result" : "");
        return parameters.Length == 0 ? result : parameters + ", " + result;
    }

    private void AddManagedResultParameter(TypeExpression type, List<string> parameters, bool named = false)
    {
        if (UsesManagedResultStorage(type))
            parameters.Add(EmitType(type) + "*" + (named ? " %gc.result" : ""));
    }

    private string? EmitValueCall(TypeExpression type, string callee, string arguments,
        bool transferArguments = true, string? callSignature = null)
    {
        if (UsesManagedResultStorage(type))
        {
            string result = CreateManagedTemporary(type);
            string argument = EmitType(type) + "* " + result;
            string args = arguments.Length == 0 ? argument : arguments + ", " + argument;
            if (transferArguments) TransferArguments();
            EmitCallInstruction("void", callee, args);
            string value = NewTemp();
            Emit($"    {value} = load {EmitType(type)}, {EmitType(type)}* {result}");
            return value;
        }

        string returnType = callSignature ?? EmitAbiReturnType(type);
        if (transferArguments) TransferArguments();
        if (EmitType(type) == "void")
        {
            EmitCallInstruction(returnType, callee, arguments);
            return null;
        }
        string temp = NewTemp();
        EmitCallInstruction(returnType, callee, arguments, temp);
        return temp;
    }

    private void EmitCallAndPush(TypeExpression type, string callee, string arguments, string? callSignature = null)
    {
        string? value = EmitValueCall(type, callee, arguments, callSignature: callSignature);
        if (value != null) Push(value);
    }
}
