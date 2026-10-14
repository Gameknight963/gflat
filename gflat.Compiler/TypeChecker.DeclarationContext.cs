using gflat.ast;


namespace gflat;

public partial class TypeChecker
{
    private IDisposable IsolateDeclarationContext()
    {
        static T[] Save<T>(Stack<T> stack) { var saved = stack.ToArray(); stack.Clear(); return saved; }
        static void Restore<T>(Stack<T> stack, T[] saved) { stack.Clear(); foreach (var item in saved.Reverse()) stack.Push(item); }
        var variables = Save(_scopes); var aliases = Save(_localAliases); var enums = Save(_localEnums);
        var constants = Save(localConstants);
        var boundaries = Save(_lambdaScopeBoundaries); var statics = Save(_lambdaStaticStack); var returns = Save(_actualReturnTypes);
        var function = _currentFunction; var constructor = _currentConstructor;
        int loop = _loopDepth; bool defer = _inDefer;
        _currentFunction = null; _currentConstructor = null; _loopDepth = 0; _inDefer = false;

        return new NullableAction(() => {
            Restore(_scopes, variables); Restore(_localAliases, aliases); Restore(_localEnums, enums);
            Restore(localConstants, constants);
            Restore(_lambdaScopeBoundaries, boundaries); Restore(_lambdaStaticStack, statics); Restore(_actualReturnTypes, returns);
            _currentFunction = function; _currentConstructor = constructor; _loopDepth = loop; _inDefer = defer;
        });
    }

}
