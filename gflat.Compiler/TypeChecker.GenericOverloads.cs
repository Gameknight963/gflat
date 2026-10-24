using System.Globalization;
using System.Text;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    // Parameters belong to a private signature preview, never a published method.
    private sealed record CallableOverloadCandidate(MethodDeclaration Definition,
        List<TypeExpression> TypeArguments, List<Parameter> Parameters);

    private const string OverloadPreviewPrefix = "$overload-preview$";
    private const string OverloadPatternPrefix = "$overload-pattern$";
    private static readonly IReadOnlyDictionary<string, string> EmptyCallableTypeNames = new Dictionary<string, string>();

    private CallableOverloadCandidate SelectCallableOverload(IEnumerable<MethodDeclaration> definitions,
        CallExpression call, string name,
        IReadOnlyDictionary<MethodDeclaration, IReadOnlyDictionary<string, TypeExpression>>? seeds = null)
    {
        var methods = definitions.Distinct().ToArray();
        foreach (var argument in call.Arguments)
            if (!_types.ContainsKey(argument)) argument.Accept(this);
        // Explicit arguments bind in the caller, before entering any definition scope.
        var explicitTypes = call.TypeArguments.Select(ResolveAlias).ToList();
        var candidates = new List<CallableOverloadCandidate>();
        string? missingParameter = null;
        (MethodDeclaration Method, GenericParameter Parameter, TypeExpression Argument)? failedConstraint = null;
        foreach (var method in methods)
        {
            if (explicitTypes.Count != 0 && (!method.IsGeneric || method.GenericParameters.Count != explicitTypes.Count)) continue;
            if (!AcceptsArgumentCount(method.Parameters, call.Arguments.Count)) continue;
            using var context = EnterMethodSignatureContext(method);
            var names = method.GenericParameters.Select(parameter => parameter.Name).ToHashSet();
            var bindings = new Dictionary<string, TypeExpression>();
            if (explicitTypes.Count != 0)
            {
                foreach (var pair in method.GenericParameters.Zip(explicitTypes)) bindings[pair.First.Name] = pair.Second;
            }
            else if (method.IsGeneric)
            {
                if (seeds != null && seeds.TryGetValue(method, out var initial))
                    foreach (var binding in initial.Where(pair => names.Contains(pair.Key))) bindings[binding.Key] = binding.Value;
                bool consistent = true;
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    var argument = call.Arguments[i];
                    if (NeedsCreationTarget(argument) || IsOverloadedFunctionValue(argument) ||
                        argument is DefaultExpression { TargetType: null } ||
                        GetType(argument) is NamedTypeExpression { Name: "null" }) continue;
                    if (!InferCallableBindings(GenericFormalType(method, method.Parameters[i].Type), GetType(argument), names, bindings))
                    { consistent = false; break; }
                }
                if (!consistent)
                {
                    missingParameter ??= method.GenericParameters.FirstOrDefault(parameter => !bindings.ContainsKey(parameter.Name))?.Name;
                    continue;
                }
            }
            var absent = method.GenericParameters.FirstOrDefault(parameter => !bindings.ContainsKey(parameter.Name));
            if (absent != null) { missingParameter ??= absent.Name; continue; }
            bool constraintsSatisfied = true;
            foreach (var parameter in method.GenericParameters)
            {
                if (parameter.Constraint == null) continue;
                var constraint = SubstituteExtensionType(GenericFormalType(method, parameter.Constraint), bindings);
                if (!(methods.Length > 1 && DeclaredConstraintSatisfied(bindings[parameter.Name], constraint) == false) &&
                    FunctionConstraintSatisfied(method, bindings[parameter.Name], constraint)) continue;
                failedConstraint ??= (method, parameter, bindings[parameter.Name]);
                constraintsSatisfied = false;
                break;
            }
            if (!constraintsSatisfied) continue;
            var parameters = method.Parameters.Select(parameter =>
            {
                var closed = BindCallableOwnerType(method, call.Callee,
                    SubstituteExtensionType(GenericFormalType(method, parameter.Type), bindings));
                return new Parameter(parameter.Name, CallablePreviewType(method, methods.Length > 1 ? OverloadArgumentType(closed) : closed),
                    parameter.Line, parameter.IsConst, parameter.DefaultValue)
                    { Span = parameter.Span, IsExtensionReceiver = parameter.IsExtensionReceiver };
            }).ToList();
            candidates.Add(new(method, method.GenericParameters.Select(parameter => bindings[parameter.Name]).ToList(), parameters));
        }
        if (candidates.Count == 0 && methods.Length == 1)
        {
            var method = methods[0];
            if (explicitTypes.Count != 0 && !method.IsGeneric)
                throw new TypeCheckException($"Function '{name}' is not generic", call.Span);
            if (explicitTypes.Count != 0 && explicitTypes.Count != method.GenericParameters.Count)
                throw new TypeCheckException($"Generic function '{name}' expects {method.GenericParameters.Count} type arguments, but got {explicitTypes.Count}", call.Span);
            if (!AcceptsArgumentCount(method.Parameters, call.Arguments.Count))
                throw new TypeCheckException($"Function '{name}' expects {method.Parameters.Count} arguments but got {call.Arguments.Count}", call.Span);
            if (missingParameter != null)
                throw new TypeCheckException($"Could not infer type parameter '{missingParameter}' for generic function '{name}'", call.Span);
            if (failedConstraint is { } failure)
                throw new TypeCheckException($"Type '{DisplayTypeName(failure.Argument)}' does not satisfy constraint '{DisplayTypeName(failure.Parameter.Constraint!)}' for type parameter '{failure.Parameter.Name}' on function '{DisplayName(failure.Method.Name)}'", call.Span);
        }
        // The winner is instantiated by the caller. Contextual new()/function
        // groups must bind to its real signature, never these private proxies.
        if (methods.Length == 1 && candidates.Count == 1) return candidates[0];
        return SelectBestOverload(candidates, candidate => candidate.Parameters, call.Arguments, name, call.Line,
            preferWhenTied: PreferCallableOverload, bindContextualArguments: false, useDeclaredTypes: methods.Length > 1);
    }

    private bool InferCallableBindings(TypeExpression formal, TypeExpression actual, ISet<string> names,
        Dictionary<string, TypeExpression> bindings, bool pattern = false)
    {
        if (IsCreationPlaceholder(actual)) return true;
        if (formal is NamedTypeExpression { IsResolvedNominal: false, Namespace: null, TypeArguments.Count: 0 } variable && names.Contains(variable.Name))
        {
            if (!pattern && actual is ReferenceTypeExpression) return false;
            var resolved = pattern ? actual : ResolveAlias(actual);
            var inferred = resolved;
            if (variable.IsImmutableValue) inferred = inferred.WithoutValueProtection();
            else if (variable.IsReadOnlyValue) inferred = inferred.WithReadOnlyValue(false);
            if (bindings.TryGetValue(variable.Name, out var previous))
                return pattern ? CallableSymbolicTypeKey(previous, EmptyCallableTypeNames, evaluateBounds: false).Equals(CallableSymbolicTypeKey(inferred, EmptyCallableTypeNames, evaluateBounds: false), StringComparison.Ordinal)
                    : TypesMatch(OverloadArgumentType(previous.WithQualifiersFrom(variable)),
                        OverloadArgumentType(resolved.WithQualifiersFrom(variable)));
            bindings[variable.Name] = inferred;
            return true;
        }
        if (!HasUnboundExtensionType(formal, names))
            return !pattern || CallableSymbolicTypeKey(formal, EmptyCallableTypeNames, evaluateBounds: false).Equals(CallableSymbolicTypeKey(actual, EmptyCallableTypeNames, evaluateBounds: false), StringComparison.Ordinal);
        bool Child(TypeExpression first, TypeExpression second) => InferCallableBindings(first, second, names, bindings, pattern);
        switch (formal, actual)
        {
            case (ReferenceTypeExpression first, ReferenceTypeExpression second): return Child(first.Inner, second.Inner);
            case (PointerTypeExpression first, PointerTypeExpression second): return Child(first.Inner, second.Inner);
            case (PointerTypeExpression first, ArrayTypeExpression second) when !pattern: return Child(first.Inner, second.ElementType);
            case (ManagedTypeExpression first, ManagedTypeExpression second):
                return Child(first.Inner, second.Inner) && Child(CallableCollectorFormal(first), CallableCollectorFormal(second));
            case (ArrayTypeExpression first, ArrayTypeExpression second):
                return Child(first.ElementType, second.ElementType) && (!pattern ||
                    CallableArrayExtentKey(first, EmptyCallableTypeNames, false) == CallableArrayExtentKey(second, EmptyCallableTypeNames, false));
            case (FunctionPointerTypeExpression first, FunctionPointerTypeExpression second):
                return first.IsManaged == second.IsManaged && first.ParameterTypes.Count == second.ParameterTypes.Count &&
                    Child(first.ReturnType, second.ReturnType) && first.ParameterTypes.Zip(second.ParameterTypes).All(pair => Child(pair.First, pair.Second));
            case (NamedTypeExpression first, NamedTypeExpression second) when first.TypeArguments.Count > 0:
            {
                var origin = second.GenericUse ?? extensionGenericTypeOrigins.GetValueOrDefault(second.Name) ?? second;
                return first.Name == origin.Name && first.Namespace == origin.Namespace && first.TypeArguments.Count == origin.TypeArguments.Count &&
                    first.TypeArguments.Zip(origin.TypeArguments).All(pair => Child(pair.First, pair.Second));
            }
            case (NestedTypeExpression first, NamedTypeExpression second):
            {
                var origin = second.GenericUse ?? extensionGenericTypeOrigins.GetValueOrDefault(second.Name) ?? second;
                int separator = origin.Name.LastIndexOf('.');
                return separator >= 0 && origin.Name[(separator + 1)..] == first.Member &&
                    first.TypeArguments.Count == origin.TypeArguments.Count &&
                    Child(first.Parent, NamedTypeExpression.Resolved(origin.Name[..separator], second.Line)) &&
                    first.TypeArguments.Zip(origin.TypeArguments).All(pair => Child(pair.First, pair.Second));
            }
        }
        return false;
    }

    private TypeExpression CallableCollectorFormal(ManagedTypeExpression managed)
        => managed.Collector ?? (_globalScope.Aliases.GetValueOrDefault("GC") ?? new NamedTypeExpression("GC", null, managed.Line));

    private bool PreferCallableOverload(CallableOverloadCandidate first, CallableOverloadCandidate second)
    {
        if (first.Definition.IsGeneric != second.Definition.IsGeneric) return !first.Definition.IsGeneric;
        if (!first.Definition.IsGeneric) return false;
        return PatternAccepts(second.Definition, first.Definition) && !PatternAccepts(first.Definition, second.Definition);
    }

    private bool PatternAccepts(MethodDeclaration formal, MethodDeclaration actual)
    {
        if (formal.Parameters.Count != actual.Parameters.Count) return false;
        using var context = EnterMethodSignatureContext(formal);
        var names = formal.GenericParameters.Select(parameter => parameter.Name).ToHashSet();
        var actualBindings = actual.GenericParameters.Select((parameter, index) => (parameter.Name,
            Type: (TypeExpression)NamedTypeExpression.Resolved(OverloadPatternPrefix + index, parameter.Line)))
            .ToDictionary(pair => pair.Name, pair => pair.Type);
        var inferred = new Dictionary<string, TypeExpression>();
        foreach (var pair in formal.Parameters.Zip(actual.Parameters))
        {
            var first = RemoveCallableProtection(GenericFormalType(formal, pair.First.Type));
            var second = RemoveCallableProtection(SubstituteExtensionType(GenericFormalType(actual, pair.Second.Type), actualBindings));
            if (!InferCallableBindings(first, second, names, inferred, pattern: true)) return false;
        }
        return true;
    }

    private TypeExpression CallablePreviewType(MethodDeclaration method, TypeExpression type)
    {
        TypeExpression result = type switch
        {
            NamedTypeExpression { TypeArguments.Count: > 0 } => KnownExtensionType(type) ?? CallableOpaquePreview(type),
            NestedTypeExpression nested => KnownExtensionType(nested) ?? KnownNestedPreview(nested) ?? CallableOpaquePreview(type),
            ReferenceTypeExpression reference => new ReferenceTypeExpression(CallablePreviewType(method, reference.Inner), reference.Line),
            PointerTypeExpression pointer => new PointerTypeExpression(CallablePreviewType(method, pointer.Inner), pointer.IsNullable, pointer.Line, pointer.IsReadOnly),
            ManagedTypeExpression managed => new ManagedTypeExpression(CallablePreviewType(method, managed.Inner), managed.IsNullable, managed.Line,
                managed.IsReadOnly, CallablePreviewType(method, GenericFormalType(method, CallableCollectorFormal(managed)))) { UsesDefaultCollector = managed.UsesDefaultCollector },
            ArrayTypeExpression array => new ArrayTypeExpression(CallablePreviewType(method, array.ElementType), ResolveArrayLength(array), array.Line),
            FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(CallablePreviewType(method, function.ReturnType),
                function.ParameterTypes.Select(parameter => CallablePreviewType(method, parameter)).ToList(), function.IsManaged,
                function.IsNullable, function.Line, function.IsSignature),
            _ => type
        };
        result.Span = type.Span;
        result = result.WithQualifiersFrom(type);
        if (type.GenericParameterView != null) result = result.WithGenericParameterView(type.GenericParameterView);
        return result;

        TypeExpression? KnownNestedPreview(NestedTypeExpression nested)
        {
            if (KnownExtensionType(nested.Parent) is not NamedTypeExpression parent || nested.TypeArguments.Count != 0) return null;
            string identity = parent.Name + "." + nested.Member;
            return _classes.ContainsKey(identity) || _structs.ContainsKey(identity) || _interfaces.ContainsKey(identity) || _enums.ContainsKey(identity)
                ? NamedTypeExpression.Resolved(identity, nested.Line) : null;
        }
    }

    private TypeExpression CallableOpaquePreview(TypeExpression type) => NamedTypeExpression.Resolved(OverloadPreviewPrefix +
        Convert.ToHexString(Encoding.UTF8.GetBytes(CallableSymbolicTypeKey(type, EmptyCallableTypeNames, evaluateBounds: false))), type.Line).WithQualifiersFrom(type);

    private string GenericOverloadKey(MethodDeclaration method)
    {
        using var context = EnterMethodSignatureContext(method);
        var names = method.GenericParameters.Select((parameter, index) => (parameter.Name, index))
            .ToDictionary(pair => pair.Name, pair => "$type" + pair.index);
        return method.GenericParameters.Count + ":" + string.Join(",", method.Parameters.Select(parameter =>
            CallableSymbolicTypeKey(OverloadArgumentType(GenericFormalType(method, parameter.Type)), names)));
    }

    private string GenericOverloadDefinitionSuffix(MethodDeclaration method)
        => overloadFamilies.TryGetValue(method, out var family) && family.Count > 1
            ? "$generic$" + Convert.ToHexString(Encoding.UTF8.GetBytes(GenericOverloadKey(method))) : "";

    private string CallableSymbolicTypeKey(TypeExpression type, IReadOnlyDictionary<string, string> names, bool evaluateBounds = true)
    {
        if (type.GenericParameterView != null) type = OverloadArgumentType(type);
        string Child(TypeExpression inner) => CallableSymbolicTypeKey(inner, names, evaluateBounds);
        return type switch
        {
            NamedTypeExpression named when !named.IsResolvedNominal && named.Namespace == null && names.TryGetValue(named.Name, out var variable) => variable,
            NamedTypeExpression named => (named.Namespace == null ? "" : named.Namespace + "::") + named.Name +
                (named.TypeArguments.Count == 0 ? "" : "<" + string.Join(",", named.TypeArguments.Select(Child)) + ">"),
            ReferenceTypeExpression reference => "ref(" + Child(reference.Inner) + ")",
            PointerTypeExpression pointer => "ptr" + (pointer.IsNullable ? "?" : "!") + "(" + Child(pointer.Inner) + ")",
            ManagedTypeExpression managed => "managed" + (managed.IsNullable ? "?" : "!") + "(" + Child(managed.Inner) + "," + Child(CallableCollectorFormal(managed)) + ")",
            ArrayTypeExpression array => "array(" + Child(array.ElementType) + "," + CallableArrayExtentKey(array, names, evaluateBounds) + ")",
            FunctionPointerTypeExpression function => (function.IsSignature ? "signature" : function.IsManaged ? "managed-function" : "function") +
                (function.IsNullable ? "?" : "!") + "(" + Child(function.ReturnType) + ";" + string.Join(",", function.ParameterTypes.Select(Child)) + ")",
            NestedTypeExpression nested => Child(nested.Parent) + "::" + nested.Member +
                (nested.TypeArguments.Count == 0 ? "" : "<" + string.Join(",", nested.TypeArguments.Select(Child)) + ">"),
            _ => TypeName(type.WithoutValueProtection())
        };
    }

    private string CallableArrayExtentKey(ArrayTypeExpression array, IReadOnlyDictionary<string, string> names, bool evaluate)
    {
        if (array.Size is { } size) return size.ToString(CultureInfo.InvariantCulture);
        if (array.SizeExpression == null) return "unsized";
        var tokens = CallableBoundTokens(array.SizeExpression);
        if (evaluate && !tokens.Any(token => token.Kind == TokenKind.Identifier && names.ContainsKey(token.Text)))
            return ResolveArrayLength(array)?.ToString(CultureInfo.InvariantCulture) ?? "unsized";
        return string.Join(";", tokens.Select(token => ((int)token.Kind).ToString(CultureInfo.InvariantCulture) + ":" +
            (token.Kind == TokenKind.Identifier && names.TryGetValue(token.Text, out var name) ? name : token.Text)));
    }

    private static List<Token> CallableBoundTokens(AstNode expression)
    {
        if (expression.Span.Source is { } source && expression.Span.Length > 0)
            return Lexer.Tokenize(source.Text.Substring(expression.Span.Start, expression.Span.Length))
                .Where(token => token.Kind != TokenKind.EndOfFile).ToList();
        if (expression is LiteralExpression literal) return [literal.Token];
        return [new Token(TokenKind.Identifier, expression.Line, 0, 0, expression.GetType().Name)];
    }

    private static TypeExpression RemoveCallableProtection(TypeExpression type)
    {
        TypeExpression result = type switch
        {
            NamedTypeExpression named => new NamedTypeExpression(named.Name, named.Namespace, named.Line,
                named.TypeArguments.Select(RemoveCallableProtection).ToList()) { NameSpan = named.NameSpan },
            ReferenceTypeExpression reference => new ReferenceTypeExpression(RemoveCallableProtection(reference.Inner), reference.Line),
            PointerTypeExpression pointer => new PointerTypeExpression(RemoveCallableProtection(pointer.Inner), pointer.IsNullable, pointer.Line),
            ManagedTypeExpression managed => new ManagedTypeExpression(RemoveCallableProtection(managed.Inner), managed.IsNullable, managed.Line,
                collector: managed.Collector == null ? null : RemoveCallableProtection(managed.Collector)) { UsesDefaultCollector = managed.UsesDefaultCollector },
            ArrayTypeExpression array => new ArrayTypeExpression(RemoveCallableProtection(array.ElementType), array.Size, array.Line, array.SizeExpression),
            FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(RemoveCallableProtection(function.ReturnType),
                function.ParameterTypes.Select(RemoveCallableProtection).ToList(), function.IsManaged, function.IsNullable, function.Line, function.IsSignature),
            NestedTypeExpression nested => new NestedTypeExpression(RemoveCallableProtection(nested.Parent), nested.Member,
                nested.TypeArguments.Select(RemoveCallableProtection).ToList(), nested.Line) { NameSpan = nested.NameSpan },
            _ => type.WithoutValueProtection()
        };
        if (result is NamedTypeExpression name && type is NamedTypeExpression { IsResolvedNominal: true }) result = name.AsResolvedNominal();
        result.Span = type.Span;
        if (type.GenericParameterView != null) result = result.WithGenericParameterView(type.GenericParameterView);
        return result;
    }
}
