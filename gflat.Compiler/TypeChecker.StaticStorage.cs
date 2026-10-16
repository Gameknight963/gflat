using gflat.ast;
using gflat.CompileExceptions;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    public sealed record StaticStorage(FieldDeclaration Field, string? Owner, string Namespace, string Symbol);
    private readonly Dictionary<FieldDeclaration, StaticStorage> staticStorage = new();
    private readonly Dictionary<AstNode, FieldDeclaration> staticStorageReads = new();
    private readonly Dictionary<MethodDeclaration, string> staticConstructors = new();
    private readonly Dictionary<FieldDeclaration, ConstValue> staticStorageConstants = new();
    public bool TryGetStaticInitializer(FieldDeclaration field, out ConstValue? value) => staticStorageConstants.TryGetValue(field, out value);
    public IEnumerable<StaticStorage> GetStaticStorage() => staticStorage.Values.OrderBy(s => s.Field.Span.Source?.Path, StringComparer.Ordinal).ThenBy(s => s.Field.Span.Start);
    public FieldDeclaration? GetStaticStorage(AstNode expression) => staticStorageReads.GetValueOrDefault(expression);
    public StaticStorage GetStaticStorageInfo(FieldDeclaration field) => staticStorage[field];
    public IReadOnlyList<AstNode> GetStaticInitializers()
    {
        var entries = GetStaticStorage().Where(s => !s.Field.IsConst &&
            !staticStorageConstants.ContainsKey(s.Field) && (s.Field.Initializer != null || ContainsManagedReference(s.Field.Type) || HasDestructor(s.Field.Type)))
            .Select(s => (Node: (AstNode)s.Field, Path: s.Field.Span.Source?.Path, Position: s.Field.Span.Start)).ToList();
        foreach (var (method, owner) in staticConstructors)
        {
            var call = new CallExpression(new IdentifierExpression(method.Name, method.Line), [], method.Line) { Span = method.Span };
            _resolvedCalls[call] = method;
            RecordType(call, Void);
            int position = Math.Max(method.Span.Start, staticStorage.Values.Where(s => s.Owner == owner)
                .Select(s => s.Field.Span.Start + 1).DefaultIfEmpty(0).Max());
            entries.Add((new ExpressionStatement(call, method.Line) { Span = method.Span }, method.Span.Source?.Path, position));
        }
        return entries.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Position).Select(e => e.Node).ToArray();
    }

    private void RegisterStaticStorage(CompilationUnit unit)
    {
        Walk(unit.Members, "", null);
        foreach (var ns in unit.Namespaces) Walk(ns.Members, ns.Name.Replace("::", "$"), null);
        void Walk(IEnumerable<AstNode> members, string ns, string? owner)
        {
            foreach (var member in members)
                switch (member)
                {
                    case NamespaceDeclaration child:
                        Walk(child.Members, ns.Length == 0 ? child.Name : ns + "$" + child.Name, null); break;
                    case ClassDeclaration { IsGeneric: false } cls: Walk(cls.Members, ns, cls.Name); break;
                    case StructDeclaration { IsGeneric: false } str: Walk(str.Members, ns, str.Name); break;
                    case FieldDeclaration field when owner == null || field.IsStatic || field.IsConst:
                        staticStorage[field] = new(field, owner, ns,
                            "gflat$storage$" + (owner ?? ns) + "$" + field.Name);
                        break;
                    case MethodDeclaration { StaticConstructorName: not null } method when owner != null:
                        staticConstructors[method] = owner;
                        break;
                }
        }
    }

    private void RegisterStaticTypeStorage(IEnumerable<AstNode> members, string owner)
    {
        foreach (var member in members)
        {
            if (member is FieldDeclaration field && (field.IsStatic || field.IsConst))
                staticStorage.TryAdd(field, new(field, owner, _currentNamespacePath.Replace("::", "$"), "gflat$storage$" + owner + "$" + field.Name));
            if (member is MethodDeclaration { StaticConstructorName: not null } method)
                staticConstructors[method] = owner;
        }
    }

    private FieldDeclaration? FindStaticStorage(string name)
    {
        if (_scopes.Any(s => s.ContainsKey(name))) return null;
        var field = _currentClass?.FieldDeclarationsByName.GetValueOrDefault(name) ??
            _currentStruct?.FieldDeclarationsByName.GetValueOrDefault(name);
        if (field != null) return field.IsStatic ? field : null;
        return ResolveGlobalField(name);
    }

    private bool TryBindStaticStorage(AstNode expression)
    {
        FieldDeclaration? field = null;
        string? owner = null;
        if (expression is IdentifierExpression id) field = FindStaticStorage(id.Name);
        else if (expression is NamespaceAccessExpression access)
        {
            if (access.Left is IdentifierExpression left && TryLookupVariable(left.Name, out _)) return false;
            if (StaticOwnerType(access.Left) is { } type && ResolveAlias(type) is NamedTypeExpression named)
            {
                field = GetClass(named.Name)?.FieldDeclarationsByName.GetValueOrDefault(access.Member) ??
                    GetStruct(named.Name)?.FieldDeclarationsByName.GetValueOrDefault(access.Member);
                if (field != null && !field.IsStatic && !field.IsConst)
                    throw new TypeCheckException($"Instance field '{access.Member}' must be accessed through an object", access.Span);
                if (field != null) owner = named.Name;
            }
            field ??= ResolveNamespace(access.Left)?.Fields.GetValueOrDefault(access.Member);
        }
        if (field == null || !staticStorage.ContainsKey(field)) return false;
        if (expression is NamespaceAccessExpression qualified) ValidateAliasMember(qualified);
        owner = staticStorage[field].Owner;
        if (owner == null && field.Accessibility == TokenKind.Private && field.Span.SourceId != expression.Span.SourceId)
            throw new TypeCheckException($"Global field '{field.Name}' is private to its source file", expression.Span);
        if (owner != null && field.Accessibility == TokenKind.Private &&
            !CanAccessPrivate(_currentClass?.Name ?? _currentStruct?.Name, owner))
            throw new TypeCheckException($"Cannot access private field '{field.Name}'", expression.Span);
        if (owner != null && field.Accessibility == TokenKind.Protected &&
            (_currentClass == null || !IsSubclassOf(_currentClass.Name, owner)))
            throw new TypeCheckException($"Cannot access protected field '{field.Name}'", expression.Span);
        staticStorageReads[expression] = field;
        var fieldType = ResolveAlias(field.Type);
        RecordType(expression, field.IsConst ? TypeQualifiers.ReadOnly(fieldType) : fieldType);
        if (field.IsConst && EvaluateDefaultConstant(field, out var constant)) _constValues[expression] = constant!;
        return true;
    }

    private void CheckStaticField(FieldDeclaration field)
    {
        using var isolation = IsolateDeclarationContext();
        _currentFunction = new MethodDeclaration("$initializer", Void, [], null, TokenKind.Private,
            true, false, false, false, field.Line);
        field.Accept(this);
    }

    private void RecordStaticInitializer(FieldDeclaration field)
    {
        if (!staticStorage.ContainsKey(field) || field.IsConst || field.Initializer == null ||
            HasDestructor(field.Type) || ContainsManagedReference(field.Type)) return;
        if (_constEvaluator.TryEvaluate(field.Initializer, field.Type, out var value, out _))
            staticStorageConstants[field] = NormalizeStringBufferConstant(value!, ResolveAlias(field.Type));
    }

    private void ValidateStaticStorage()
    {
        foreach (var storage in staticStorage.Values)
        {
            var field = storage.Field;
            if (!field.IsConst && field.Initializer == null && !CanDefaultInitialize(field.Type))
                AnalyzeSafely(() => throw new TypeCheckException(
                    $"Static or global field '{field.Name}' requires an initializer", field.Span));
        }
    }

    private void ValidateStaticConstructor(MethodDeclaration method)
    {
        if (method.StaticConstructorName == null) return;
        string? owner = _currentClass?.Name ?? _currentStruct?.Name;
        string localOwner = owner?.Split('.').Last() ?? "";
        string name = method.StaticConstructorName;
        if (owner == null || !(localOwner == name || localOwner.EndsWith("$" + name) ||
            localOwner.StartsWith(name + "$") || localOwner.Contains("$" + name + "$")))
            throw new TypeCheckException("Static constructor name must match its containing type", method.Span);
    }
}
