using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<MethodDeclaration, MethodDeclaration> extensionMethodSources = new();
    public MethodDeclaration? GetExtensionMethodSource(MethodDeclaration method) => extensionMethodSources.GetValueOrDefault(method);
    public static string ExtensionMemberName(MethodDeclaration method) => method.ExtensionMemberName ?? method.Name;

    private void ExpandExtensionBlocks(CompilationUnit unit)
    {
        int helperId = 0;
        Expand(unit.Members);
        foreach (var ns in unit.Namespaces) Expand(ns.Members);
        void Expand(List<AstNode> members)
        {
            for (int index = 0; index < members.Count; index++)
            {
                if (members[index] is NamespaceDeclaration ns) { Expand(ns.Members); continue; }
                if (members[index] is not ExtensionDeclaration block) continue;
                var methods = new List<AstNode>();
                foreach (var method in block.Members.Cast<MethodDeclaration>())
                {
                    var names = block.GenericParameters.Concat(method.GenericParameters).Select(p => p.Name).ToArray();
                    if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                        throw new TypeCheckException("Extension block and method type parameters must have different names", method.Span);
                    method.ExtensionReceiverType = block.Target;
                    TypeExpression receiver = method.IsImmutable ? TypeQualifiers.Immutable(block.Target) : block.Target;
                    var pointer = new PointerTypeExpression(receiver, false, method.Line, method.IsReadOnly);
                    var parameters = new[] { new Parameter("this", pointer, method.Line) {
                        IsExtensionReceiver = true, Span = block.Target.Span } }.Concat(method.Parameters).ToList();
                    var helper = new MethodDeclaration("$extension$" + helperId++, method.ReturnType, parameters,
                        method.Body, method.Accessibility, false, false, false, false, method.Line,
                        method.IsReadOnly, method.IsConst, method.Attributes,
                        block.GenericParameters.Concat(method.GenericParameters).ToList(), method.IsNoThrow, method.IsImmutable) {
                        Span = method.Span, IsInline = method.IsInline, ExtensionMemberName = method.Name,
                        ExtensionReceiverType = block.Target, ExtensionBlockParameterCount = block.GenericParameters.Count };
                    extensionMethodSources[helper] = method;
                    namespaceHiddenDeclarations.Add(helper);
                    methods.Add(helper);
                }
                members.RemoveAt(index);
                members.InsertRange(index, methods);
                index += methods.Count - 1;
            }
        }
    }

    private TypeExpression ExtensionObjectPointer(TypeExpression type, bool readOnly)
    {
        if (type is PointerTypeExpression pointer) return pointer.WithoutValueProtection();
        if (type is ManagedTypeExpression managed)
            return new PointerTypeExpression(managed.Inner, false, type.Line, managed.IsReadOnly);
        return new PointerTypeExpression(type, false, type.Line, readOnly);
    }

    private bool IsExtensionObjectType(TypeExpression type)
    {
        return type is not (PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression or FunctionPointerTypeExpression) &&
            type is not NamedTypeExpression { Name: "void" or "null" };
    }
}
