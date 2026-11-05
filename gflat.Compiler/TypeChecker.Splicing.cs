using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<AstNode, AstNode> reflectionOperations = new();
    private readonly Dictionary<SpliceTypeExpression, TypeExpression> resolvedSpliceTypes = new();
    private int staticExpansionElements;
    public AstNode? GetReflectionOperation(AstNode node) => reflectionOperations.GetValueOrDefault(node);

    private bool CheckSplicedOperation(AstNode operation, SpliceExpression splice, Func<AstNode, AstNode> replace)
    {
        splice.Accept(this);
        var resolved = replace(splice.Resolved!);
        resolved.Span = operation.Span;
        reflectionOperations[operation] = resolved;
        resolved.Accept(this);
        RecordType(operation, GetType(resolved));
        return true;
    }

    private AstNode ReflectedFieldReceiver(SpliceExpression splice, ConstValue.Reflection reflection)
    {
        var receiver = splice.Receiver!;
        receiver.Accept(this);
        var originalType = ResolveAlias(GetType(receiver));
        var valueType = originalType switch { PointerTypeExpression p => ResolveAlias(p.Inner), ManagedTypeExpression m => ResolveAlias(m.Inner), _ => originalType };
        var owner = reflection.DeclaringType!;
        if (TypesMatch(valueType.WithoutValueProtection(), owner.WithoutValueProtection()))
        {
            checkedPrimitiveReceivers.Add(receiver);
            return receiver;
        }
        if (valueType is not NamedTypeExpression actual || owner is not NamedTypeExpression declared || !IsSubclassOf(actual.Name, declared.Name))
            throw new TypeCheckException($"Reflected member '{reflection.Name}' requires an instance of '{DisplayTypeName(owner)}'", splice.Span);
        var view = owner.WithQualifiersFrom(valueType);
        TypeExpression target;
        AstNode operand;
        if (originalType is ManagedTypeExpression managed)
        {
            target = new ManagedTypeExpression(view, managed.IsNullable, splice.Line, collector: managed.Collector);
            operand = receiver;
        }
        else
        {
            target = new PointerTypeExpression(view, originalType is PointerTypeExpression { IsNullable: true }, splice.Line);
            operand = originalType is PointerTypeExpression ? receiver : new UnaryExpression(receiver, TokenKind.Ampersand, true, splice.Line);
        }
        var cast = new CastExpression(target, operand, splice.Line) { Span = receiver.Span };
        if (!ReferenceEquals(operand, receiver)) operand.Accept(this);
        RecordType(cast, target);
        checkedPrimitiveReceivers.Add(cast);
        return cast;
    }
    private ConstValue.Reflection RequireReflection(AstNode expression)
    {
        expression.Accept(this);
        if (!_constEvaluator.TryEvaluate(expression, out var value, out var error) || value is not ConstValue.Reflection reflection)
            throw new TypeCheckException("A splice requires a constant reflection" + (error == null ? "" : ": " + error), expression.Span);
        return reflection;
    }

    private TypeExpression ResolveSpliceType(SpliceTypeExpression node)
    {
        if (resolvedSpliceTypes.TryGetValue(node, out var resolved)) return resolved;
        var reflection = RequireReflection(node.Reflection);
        if (reflection.Kind is not ("Type" or "Alias") || reflection.Type == null)
            throw new TypeCheckException($"A type splice requires a type or alias reflection, got {reflection.Kind}", node.Span);
        var type = reflection.Type.WithQualifiersFrom(node);
        if (aliasContracts.TryGetValue(reflection.Type, out var contract)) aliasContracts[type] = contract;
        resolvedSpliceTypes[node] = type;
        return type;
    }

    public void Visit(SpliceTypeExpression node) => ResolveSpliceType(node);

    public void Visit(SpliceExpression node)
    {
        var reflection = argumentSpliceValues.GetValueOrDefault(node) ?? RequireReflection(node.Reflection);
        if (TryBindReflectedFunctionValue(node, node, reflection)) return;
        AstNode resolved;
        if (node.Receiver != null && reflection.Kind is not ("Field" or "Method" or "OverloadGroup"))
            throw new TypeCheckException("A member splice requires a field or method reflection", node.Span);
        if (TryResolveReflectedDeclarationSplice(node, reflection, out var declared)) resolved = declared!;
        else if (reflection.Kind == "Value" && reflection.Value != null && reflection.Type != null)
            resolved = new ConstantExpression(reflection.Value, reflection.Type, node.Line);
        else if (reflection.Kind is "Field" or "Method" or "OverloadGroup")
        {
            if (node.Receiver != null) resolved = new MemberAccessExpression(ReflectedFieldReceiver(node, reflection), reflection.Name, false, node.Line);
            else if (reflection.DeclaringType != null)
                resolved = new NamespaceAccessExpression(reflection.DeclaringType, reflection.Name, node.Line);
            else resolved = new IdentifierExpression(reflection.Name, node.Line);
        }
        else throw new TypeCheckException($"A {reflection.Kind.ToLowerInvariant()} reflection cannot be spliced as a value", node.Span);
        resolved.Span = node.Span;
        node.Resolved = resolved;
        resolved.Accept(this);
        RecordType(node, GetType(resolved));
        if (TryGetConstValue(resolved, out var constant)) _constValues[node] = constant!;
    }

    public void Visit(StaticIfStatement node)
    {
        node.Condition.Accept(this);
        if (!_constEvaluator.TryEvaluate(node.Condition, out var value, out var error) || value is not ConstValue.Boolean condition)
            throw new TypeCheckException("static if requires a constant bool condition" + (error == null ? "" : ": " + error), node.Condition.Span);
        node.Statements.Clear();
        var selected = condition.Value ? node.ThenBody : node.ElseBody;
        if (selected != null) node.Statements.Add(selected);
        Visit((BlockStatement)node);
    }

    public void Visit(StaticForeachStatement node)
    {
        node.Collection.Accept(this);
        if (!_constEvaluator.TryEvaluate(node.Collection, out var value, out var error) || value is not ConstValue.Array elements)
            throw new TypeCheckException("static foreach requires a constant array" + (error == null ? "" : ": " + error), node.Collection.Span);
        staticExpansionElements += elements.Elements.Count;
        if (staticExpansionElements > 100000)
            throw new TypeCheckException("Compile-time expansion exceeded maximum step limit", node.Span);
        var sourceElement = (ResolveAlias(GetType(node.Collection)) as ArrayTypeExpression)?.ElementType
            ?? throw new TypeCheckException("static foreach requires an array sequence", node.Collection.Span);
        var iterationType = node.ElementType is NamedTypeExpression { Name: "var" } ? sourceElement : ResolveAlias(node.ElementType);
        if (!IsAssignable(iterationType, sourceElement))
            throw new TypeCheckException($"Cannot iterate '{DisplayTypeName(sourceElement)}' as '{DisplayTypeName(iterationType)}'", node.Span);
        node.Statements.Clear();
        foreach (var element in elements.Elements)
        {
            var body = (BlockStatement)new AstCloner(new()).CloneNode(node.Body);
            var initializer = new ConstantExpression(element, sourceElement, node.Line) { Span = node.Collection.Span };
            body.Statements.Insert(0, new VariableDeclaration(node.VariableName, iterationType, initializer, node.Line, isConst: true) { Span = node.Span });
            node.Statements.Add(body);
        }
        Visit((BlockStatement)node);
    }
}
