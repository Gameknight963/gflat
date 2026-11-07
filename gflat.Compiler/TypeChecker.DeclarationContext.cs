using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private DeclarationIsolation IsolateDeclarationContext() => new(this);

    private sealed class DeclarationIsolation : IDisposable
    {
        private readonly TypeChecker owner;
        private readonly Dictionary<string, TypeExpression>[] variables;
        private readonly Dictionary<string, TypeExpression>[] aliases;
        private readonly Dictionary<string, EnumInfo>[] enums;
        private readonly Dictionary<string, (ConstValue Value, VariableDeclaration Declaration)>[] constants;
        private readonly int[] boundaries;
        private readonly bool[] statics;
        private readonly List<TypeExpression>[] returns;
        private readonly AstNode? function;
        private readonly ConstructorDeclaration? constructor;
        private readonly TypeExpression? queryOwner;
        private readonly int loop;
        private readonly bool defer;

        public DeclarationIsolation(TypeChecker owner)
        {
            this.owner = owner;
            variables = Save(owner._scopes);
            aliases = Save(owner._localAliases);
            enums = Save(owner._localEnums);
            constants = Save(owner.localConstants);
            boundaries = Save(owner._lambdaScopeBoundaries);
            statics = Save(owner._lambdaStaticStack);
            returns = Save(owner._actualReturnTypes);
            function = owner._currentFunction;
            constructor = owner._currentConstructor;
            queryOwner = owner.typeQueryOwner;
            loop = owner._loopDepth;
            defer = owner._inDefer;
            owner._currentFunction = null;
            owner._currentConstructor = null;
            owner._loopDepth = 0;
            owner._inDefer = false;
            owner.typeQueryOwner = null;
        }

        private static T[] Save<T>(Stack<T> stack)
        {
            var saved = stack.ToArray();
            stack.Clear();
            return saved;
        }

        private static void Restore<T>(Stack<T> stack, T[] saved)
        {
            stack.Clear();
            for (int index = saved.Length - 1; index >= 0; index--) stack.Push(saved[index]);
        }

        public void Dispose()
        {
            Restore(owner._scopes, variables);
            Restore(owner._localAliases, aliases);
            Restore(owner._localEnums, enums);
            Restore(owner.localConstants, constants);
            Restore(owner._lambdaScopeBoundaries, boundaries);
            Restore(owner._lambdaStaticStack, statics);
            Restore(owner._actualReturnTypes, returns);
            owner._currentFunction = function;
            owner._currentConstructor = constructor;
            owner._loopDepth = loop;
            owner._inDefer = defer;
            owner.typeQueryOwner = queryOwner;
        }
    }
}
