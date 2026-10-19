using System.Text;

namespace gflat;

public partial class LlvmEmitter
{
    private sealed class FunctionEmission(StringBuilder parentOutput, bool exceptionBoundary)
    {
        public StringBuilder ParentOutput { get; } = parentOutput;
        public StringBuilder Output { get; } = new();
        public StringBuilder ManagedPrologue { get; } = new();
        public Dictionary<string, string> PendingOwnerRoots { get; } = new();
        public Stack<TryBlockInfo> Handlers { get; } = new();
        public Stack<string> CaughtHandles { get; } = new();
        public bool ExceptionBoundary { get; } = exceptionBoundary;
        public bool NeedsPersonality;
        public bool InCleanup;
        public int CleanupTryDepth;
        public int ExceptionCleanupDepth;
        public int HeaderEnd;
        public int EntryPosition;
        public string? DebugScope;
        public string? DebugLocation;
        public gflat.ast.AstNode? DebugSource;
        public string? DebugThisType;
    }

    private readonly Stack<FunctionEmission> _functions = new();
    private FunctionEmission CurrentFunction => _functions.Peek();

    private void BeginFunction(string returnType, string name, string parameters,
        bool exceptionBoundary = false, bool inline = false, bool isEntryPoint = false, gflat.ast.AstNode? source = null)
    {
        var function = new FunctionEmission(_output, exceptionBoundary);
        _functions.Push(function);
        _output = function.Output;
        string linkage = _internalizeFunctions && !isEntryPoint ? "internal " : "";
        string header = $"define {linkage}{returnType} @{name}({parameters})" + (inline ? " inlinehint" : "");
        function.HeaderEnd = header.Length;
        if (source != null && _debugSymbols)
        {
            function.DebugSource = source;
            if (parameters.StartsWith('%') && parameters.IndexOf("* %this", StringComparison.Ordinal) is int end && end > 0)
                function.DebugThisType = parameters[1..end];
            function.DebugScope = DebugFunction(name, source);
            header += " !dbg " + function.DebugScope;
        }
        Emit(header + " {");
        Emit("entry:");
        function.EntryPosition = _output.Length;
        if (source != null) SetDebugLocation(source);
    }

    private void EndFunction()
    {
        var function = _functions.Pop();
        Emit("}");
        Emit("");
        function.Output.Insert(function.EntryPosition, function.ManagedPrologue);
        if (function.NeedsPersonality)
        {
            string personality = WindowsEh ? "__CxxFrameHandler3" : "__gxx_personality_v0";
            function.Output.Insert(function.HeaderEnd, $" personality i32 (...)* @{personality}");
        }
        _output = function.ParentOutput;
        _output.Append(function.Output);
    }
}
