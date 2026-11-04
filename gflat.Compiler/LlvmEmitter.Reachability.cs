using System.Text;

namespace gflat;

public partial class LlvmEmitter
{
    private bool _registerFunctions;
    private readonly Dictionary<string, Action> _deferredFunctions = new(StringComparer.Ordinal);

    private bool DeferFunction(string name, Action emit)
    {
        if (!_registerFunctions) return false;
        var ownerClass = _currentClass;
        var ownerStruct = _currentStruct;
        string ns = _currentNamespacePath;
        int phase = _staticStoragePhase;
        _deferredFunctions.TryAdd(name, () =>
        {
            _currentClass = ownerClass;
            _currentStruct = ownerStruct;
            _currentNamespacePath = ns;
            _staticStoragePhase = phase;
            emit();
        });
        return true;
    }

    private void EmitReachableFunctions()
    {
        using var timing = PipelineTimings.Measure("emit.reachable-functions");
        var graph = new LlvmModuleGraph();
        int globals = 0, functions = 0, lambdas = 0, llvm = 0;
        var done = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        AddOutput();
        pending.Enqueue("main");
        while (pending.TryDequeue(out string? name))
        {
            if (!done.Add(name)) continue;
            if (!graph.Defines(name) && _deferredFunctions.TryGetValue(name, out var emit))
            {
                emit();
                AddOutput();
            }
            foreach (string reference in graph.References(name)) pending.Enqueue(reference);
        }

        void AddOutput()
        {
            Add(_globals, ref globals);
            Add(_output, ref functions);
            Add(_lambdaFunctions, ref lambdas);
            // Preserve inline LLVM modules together, including their callbacks.
            if (llvm < llvmFunctions.Length)
            {
                graph.Roots.UnionWith(LlvmModuleGraph.Symbols(llvmFunctions.ToString(llvm, llvmFunctions.Length - llvm)));
                llvm = llvmFunctions.Length;
            }
            foreach (string root in graph.Roots) pending.Enqueue(root);
        }
        void Add(StringBuilder builder, ref int consumed)
        {
            if (consumed == builder.Length) return;
            graph.Add(builder.ToString(consumed, builder.Length - consumed));
            consumed = builder.Length;
        }
    }

    private string TrimModule(string module)
    {
        using var timing = PipelineTimings.Measure("emit.trim-module");
        var graph = new LlvmModuleGraph();
        graph.Add(module);
        var live = graph.Reachable(LlvmModuleGraph.Symbols(llvmFunctions.ToString()));
        return graph.Render(live) + llvmFunctions;
    }
}
