using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.semantics;

public partial class DefiniteAssignmentPass
{
    private readonly HashSet<string> _requiredFields = new();
    private bool _inConstructor;

    private bool IsCurrentInstanceMethod(MethodDeclaration method)
        => (_currentClass != null && _typeChecker.GetClass(_currentClass.Name)!.Methods.Values.Any(m => m.Method == method)) ||
           (_currentStruct != null && _typeChecker.GetStruct(_currentStruct.Name)!.AllMethods.Contains(method));

    private static string? AutoPropertyStorage(MethodDeclaration method)
    {
        if (method.PropertyName == null || method.IsVirtual || method.IsOverride) return null;
        if (method.Body is BlockStatement { Statements.Count: 1 } block &&
            block.Statements[0] is ExpressionStatement { Expression: AssignmentExpression { Target: IdentifierExpression id } } &&
            id.Name.StartsWith("$property$")) return id.Name;
        return null;
    }

    private void CheckImplicitFieldInitializers(string name, int line)
    {
        ResetState();
        PushScope();
        DeclareLocal("this", new NamedTypeExpression(name, null, line), true);
        BeginConstructorFields();
        _inConstructor = false;
        PopScope();
    }

    private IEnumerable<FieldDeclaration> ConstructorFields => _currentClass != null
        ? _typeChecker.GetClass(_currentClass.Name)!.FieldDeclarations
        : _typeChecker.GetStruct(_currentStruct!.Name)!.FieldDeclarations;

    private void BeginConstructorFields(bool initialize = true)
    {
        _inConstructor = true;
        foreach (var field in ConstructorFields)
            AddRequiredField(field.Name, field.Type);
        if (initialize) CheckDeclaredFieldInitializers();
    }

    private void CheckDeclaredFieldInitializers()
    {
        // Initializers run in declaration order, before the constructor body.
        foreach (var field in ConstructorFields)
            if (field.Initializer != null)
            {
                CheckExpression(field.Initializer);
                MarkConstructorField(field.Name);
            }
    }

    private void AddRequiredField(string path, TypeExpression type)
    {
        if (_typeChecker.CanDefaultInitialize(type)) return;
        type = _typeChecker.ResolveAlias(type);
        if (type is NamedTypeExpression named && _typeChecker.GetStruct(named.Name) is TypeChecker.StructInfo { Constructors.Count: 0 } str)
        {
            foreach (var field in str.Fields) AddRequiredField(path + "." + field.Name, field.Type);
            return;
        }
        _requiredFields.Add(path);
        DeclareLocal("$init$" + path, type, false);
    }

    private string? ConstructorFieldPath(AstNode node)
    {
        if (!_inConstructor) return null;
        if (node is IdentifierExpression id)
            return id.Name == "this" ? "" : Lookup(id.Name) == null ? id.Name : null;
        if (node is MemberAccessExpression member && ConstructorFieldPath(member.Object) is string parent)
            return parent.Length == 0 ? member.Member : parent + "." + member.Member;
        return null;
    }

    private void MarkConstructorField(string path)
    {
        foreach (string field in _requiredFields)
            if (field == path || field.StartsWith(path + ".")) MarkFullyAssigned("$init$" + field);
    }

    private void RequireConstructorFields(string path, int line)
    {
        if (!_inConstructor) return;
        foreach (string field in _requiredFields)
            if ((path.Length == 0 || field == path || field.StartsWith(path + ".") || path.StartsWith(field + ".")) &&
                Lookup("$init$" + field) is { IsFullyAssigned: false })
                throw new TypeCheckException($"Field '{field}' must be initialized before use or constructor completion", line);
    }
}
