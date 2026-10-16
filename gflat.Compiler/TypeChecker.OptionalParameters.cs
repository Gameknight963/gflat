using gflat.ast;
using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record DefaultParameterContext(NamespaceScope Namespace, string Path, ClassInfo? Class, StructInfo? Struct);
    private readonly Dictionary<Parameter, DefaultParameterContext> defaultParameterContexts = new();
    private readonly Dictionary<Parameter, ConstValue?> parameterDefaults = new();
    private readonly HashSet<Parameter> checkingDefaults = new();
    private readonly Dictionary<FieldDeclaration, DefaultParameterContext> defaultConstantContexts = new();
    private readonly HashSet<FieldDeclaration> checkingDefaultConstants = new();

    private bool TryGetDefaultConstant(string name, out ConstValue? value)
    {
        value = null;
        FieldDeclaration? field;
        int separator = name.LastIndexOf("::", StringComparison.Ordinal);
        if (separator >= 0)
        {
            string owner = name[..separator], member = name[(separator + 2)..];
            field = GetClass(owner)?.FieldDeclarationsByName.GetValueOrDefault(member) ??
                GetStruct(owner)?.FieldDeclarationsByName.GetValueOrDefault(member) ?? ResolveNamespaceByName(owner)?.Fields.GetValueOrDefault(member);
        }
        else field = _currentClass?.FieldDeclarationsByName.GetValueOrDefault(name) ??
            _currentStruct?.FieldDeclarationsByName.GetValueOrDefault(name) ?? ResolveGlobalField(name);
        return field != null && EvaluateDefaultConstant(field, out value);
    }

    private bool EvaluateDefaultConstant(FieldDeclaration field, out ConstValue? value)
    {
        value = null;
        if (field is not { IsConst: true, Initializer: not null }) return false;
        if (_constValues.TryGetValue(field, out value)) return true;
        if (!checkingDefaultConstants.Add(field)) throw new TypeCheckException("Circular constant initializer", field.Span);
        var previous = new DefaultParameterContext(_currentNamespace, _currentNamespacePath, _currentClass, _currentStruct);
        var context = defaultConstantContexts.GetValueOrDefault(field) ?? previous;
        _currentNamespace = context.Namespace;
        _currentNamespacePath = context.Path;
        _currentClass = context.Class;
        _currentStruct = context.Struct;
        try
        {
            using var sourceContext = SourceContext.Enter(field.Span);
            using var nullableScope = IsolateNullableFlow();
            using var exceptionScope = IsolateExceptionHandlers();
            using var declarationContext = IsolateDeclarationContext();
            field.Initializer.Accept(this);
            if (!new ConstEvaluator(this).TryEvaluate(field.Initializer, out value, out _)) return false;
            _constValues[field] = value!;
            return true;
        }
        finally
        {
            _currentNamespace = previous.Namespace;
            _currentNamespacePath = previous.Path;
            _currentClass = previous.Class;
            _currentStruct = previous.Struct;
            checkingDefaultConstants.Remove(field);
        }
    }
    private readonly Dictionary<string, (ConstructorDeclaration Constructor, List<AstNode> Arguments)> implicitBaseCalls = new();
    public (ConstructorDeclaration Constructor, List<AstNode> Arguments)? GetImplicitBaseCall(string name) =>
        implicitBaseCalls.TryGetValue(name, out var call) ? call : null;

    private static bool AcceptsArgumentCount(List<Parameter> parameters, int count) => count <= parameters.Count &&
        parameters.Skip(count).All(p => p.DefaultValue != null);

    private void RegisterParameterDefaults(AstNode declaration, List<Parameter> parameters)
    {
        bool optional = false;
        foreach (var p in parameters)
        {
            if (p.DefaultValue == null)
            {
                if (optional) throw new TypeCheckException("Required parameters must precede optional parameters", p.Span);
                continue;
            }
            optional = true;
            if (p.Type is ReferenceTypeExpression || declaration is OperatorDeclaration || declaration is ConstructorDeclaration { IsCopy: true } ||
                declaration is MethodDeclaration { PropertyName: not null } or MethodDeclaration { StringLiteralPrefix: not null })
                throw new TypeCheckException("Default arguments are not supported for ref parameters, operators, indexers, or copy constructors", p.Span);
            defaultParameterContexts.TryAdd(p, new(_currentNamespace, declarationNamespaces.GetValueOrDefault(declaration) ?? _currentNamespacePath, _currentClass, _currentStruct));
        }
    }

    private void ValidateParameterDefaults(AstNode declaration, List<Parameter> parameters)
    {
        RegisterParameterDefaults(declaration, parameters);
        foreach (var p in parameters.Where(p => p.DefaultValue != null)) ParameterDefault(p);
    }

    private ConstValue? ParameterDefault(Parameter parameter)
    {
        if (parameterDefaults.TryGetValue(parameter, out var value)) return value;
        if (!checkingDefaults.Add(parameter)) throw new TypeCheckException("Circular optional parameter default", parameter.Span);
        var context = defaultParameterContexts.GetValueOrDefault(parameter) ?? new(_currentNamespace, _currentNamespacePath, _currentClass, _currentStruct);
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        var previousFunction = _currentFunction;
        var previousConstructor = _currentConstructor;
        var scopes = _scopes.ToArray();
        var aliases = _localAliases.ToArray();
        var enums = _localEnums.ToArray();
        _scopes.Clear();
        _localAliases.Clear();
        _localEnums.Clear();
        _currentNamespace = context.Namespace;
        _currentNamespacePath = context.Path;
        _currentClass = context.Class;
        _currentStruct = context.Struct;
        _currentFunction = null;
        _currentConstructor = null;
        try
        {
            var expression = parameter.DefaultValue!;
            using var sourceContext = SourceContext.Enter(expression.Span);
            var type = ResolveAlias(parameter.Type);
            if (RequiresCopy(type)) throw new TypeCheckException("Optional parameters cannot contain values with nontrivial copying or destruction", parameter.Span);
            if (expression is DefaultExpression && !CanDefaultInitialize(type))
                throw new TypeCheckException($"Type '{TypeName(type)}' cannot be default initialized", expression.Span);
            if (expression is DefaultExpression { TargetType: null }) RecordType(expression, type);
            else expression.Accept(this);
            if (!IsAssignable(type, GetType(expression), expression))
                throw new TypeCheckException($"Default for parameter '{parameter.Name}' cannot convert to '{TypeName(type)}'", expression.Span);
            if (expression is LiteralExpression { Token.Kind: TokenKind.Null }) value = null;
            else if (!new ConstEvaluator(this).TryEvaluate(expression, out value, out string? error))
                throw new TypeCheckException($"Default for parameter '{parameter.Name}' must be a compile-time constant: {error}", expression.Span);
            if (value != null && type is NamedTypeExpression named &&
                (NumericSemantics.IntegerType(named.Name) != null || named.Name is "float" or "double"))
                value = NumericSemantics.Cast(value, named.Name, parameter.Line);
            if (value is ConstValue.String text && type is ArrayTypeExpression { Size: not null } array)
            {
                var bytes = text.BytesFromOffset(expression.Line);
                value = new ConstValue.Array(Enumerable.Range(0, array.Size.Value)
                    .Select(i => (ConstValue)new ConstValue.Char((char)(i < bytes.Length ? bytes[i] : 0))).ToList());
            }
            parameterDefaults[parameter] = value;
            return value;
        }
        finally
        {
            _scopes.Clear();
            foreach (var scope in scopes.Reverse()) _scopes.Push(scope);
            _localAliases.Clear();
            foreach (var scope in aliases.Reverse()) _localAliases.Push(scope);
            _localEnums.Clear();
            foreach (var scope in enums.Reverse()) _localEnums.Push(scope);
            _currentNamespace = previousNamespace;
            _currentNamespacePath = previousPath;
            _currentClass = previousClass;
            _currentStruct = previousStruct;
            _currentFunction = previousFunction;
            _currentConstructor = previousConstructor;
            checkingDefaults.Remove(parameter);
        }
    }

    private void AddDefaultArguments(List<AstNode> arguments, List<Parameter> parameters, AstNode call)
    {
        if (!AcceptsArgumentCount(parameters, arguments.Count)) return;
        while (arguments.Count < parameters.Count)
        {
            var parameter = parameters[arguments.Count];
            var value = ParameterDefault(parameter);
            var expression = new DefaultExpression(parameter.Type, call.Line) { Span = call.Span with { Length = 0 } };
            RecordType(expression, ResolveAlias(parameter.Type));
            if (value != null) _constValues[expression] = value;
            arguments.Add(expression);
        }
    }
}
