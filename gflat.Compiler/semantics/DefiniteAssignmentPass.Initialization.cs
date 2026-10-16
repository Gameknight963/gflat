using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.semantics;

public partial class DefiniteAssignmentPass
{
    private readonly HashSet<string> _requiredFields = new();
    private readonly Dictionary<string, (string Path, bool IsProperty)> _requiredFieldDisplay = new();
    private bool _inConstructor;

    private bool IsCurrentInstanceMethod(MethodDeclaration method)
        => (_currentClass != null && _typeChecker.GetClassByIdentity(_currentClass.Name)!.Methods.Values.Any(m => m.Method == method)) ||
           (_currentStruct != null && _typeChecker.GetStructByIdentity(_currentStruct.Name)!.AllMethods.Contains(method));

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
        DeclareLocal("this", NamedTypeExpression.Resolved(name, line), true);
        BeginConstructorFields();
        _inConstructor = false;
        PopScope();
    }

    private IEnumerable<FieldDeclaration> ConstructorFields => _currentClass != null
        ? _typeChecker.GetClassByIdentity(_currentClass.Name)!.FieldDeclarations
        : _typeChecker.GetStructByIdentity(_currentStruct!.Name)!.FieldDeclarations;

    private void BeginConstructorFields(bool initialize = true)
    {
        _inConstructor = true;
        foreach (var field in ConstructorFields.Where(f => !f.IsStatic))
            AddRequiredField(field.Name, field.Type, field.PropertyName ?? field.Name, field.PropertyName != null);
        if (initialize) CheckDeclaredFieldInitializers();
    }

    private void CheckDeclaredFieldInitializers()
    {
        // Initializers run in declaration order, before the constructor body.
        foreach (var field in ConstructorFields.Where(f => !f.IsStatic))
            if (field.Initializer != null)
            {
                CheckValue(field.Initializer);
                MarkConstructorField(field.Name);
            }
    }

    private void AddRequiredField(string path, TypeExpression type, string displayPath, bool isProperty)
    {
        if (_typeChecker.CanDefaultInitialize(type)) return;
        type = _typeChecker.ResolveAlias(type);
        if (type is NamedTypeExpression named && _typeChecker.GetStructByIdentity(named.Name) is TypeChecker.StructInfo { Constructors.Count: 0 } str)
        {
            foreach (var field in str.Fields)
            {
                string? property = str.FieldDeclarationsByName.GetValueOrDefault(field.Name)?.PropertyName;
                AddRequiredField(path + "." + field.Name, field.Type, displayPath + "." + (property ?? field.Name), property != null);
            }
            return;
        }
        _requiredFields.Add(path);
        _requiredFieldDisplay[path] = (displayPath, isProperty);
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
            {
                var display = _requiredFieldDisplay[field];
                throw new TypeCheckException($"{(display.IsProperty ? "Property" : "Field")} '{display.Path}' must be initialized before use or constructor completion", line);
            }
    }
}
