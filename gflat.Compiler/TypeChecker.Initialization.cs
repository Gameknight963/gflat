using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public bool CanDefaultInitialize(TypeExpression type) => CanDefaultInitialize(type, new HashSet<string>());

    private bool CanDefaultInitialize(TypeExpression type, HashSet<string> visiting, bool isBase = false)
    {
        type = ResolveAlias(type);
        if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression)
            return IsNullable(type);
        if (type is ArrayTypeExpression array)
            return array.Size != null && CanDefaultInitialize(array.ElementType, visiting);
        if (type is not NamedTypeExpression named) return false;
        if (named.Name is "void" or "default" or "null") return false;
        if (!visiting.Add(named.Name)) return false;
        try
        {
            if (GetStructByIdentity(named.Name) is StructInfo str)
                return str.Constructors.Count == 0 && str.Fields.All(f => CanDefaultInitialize(f.Type, visiting));
            if (GetClassByIdentity(named.Name) is ClassInfo cls)
                return (!cls.IsAbstract || isBase) && cls.Constructors.Count == 0 &&
                    (cls.BaseClass == null || CanDefaultInitialize(NamedTypeExpression.Resolved(cls.BaseClass, type.Line), visiting, isBase: true)) &&
                    cls.Fields.All(f => CanDefaultInitialize(f.Type, visiting));
            return !IsInterface(named);
        }
        finally { visiting.Remove(named.Name); }
    }

    private void ValidateImplicitConstruction(string name, int line)
    {
        IEnumerable<FieldDeclaration> fields;
        if (GetClassByIdentity(name) is ClassInfo cls)
        {
            fields = cls.FieldDeclarations;
            if (cls.BaseClass != null && GetClassByIdentity(cls.BaseClass) is ClassInfo parent)
            {
                if (parent.Constructors.Count == 0) ValidateImplicitConstruction(parent.Name, line);
                else if (!parent.Constructors.Any(c => AcceptsArgumentCount(c.Parameters, 0)))
                    throw new TypeCheckException($"Base class '{DisplayName(parent.Name)}' requires an explicit constructor call", line);
                else
                {
                    var arguments = new List<AstNode>();
                    var constructor = SelectBestOverload(parent.Constructors, c => c.Parameters, arguments, parent.Name, line);
                    // The generated constructor belongs to this derived type,
                    // independently of who called new on it.
                    ValidateConstructorAccess(constructor, parent.Name, name, SourceContext.ForLine(line));
                    AddDefaultArguments(arguments, constructor.Parameters, new DefaultExpression(null, line));
                    implicitBaseCalls[name] = (constructor, arguments);
                }
            }
        }
        else fields = GetStructByIdentity(name)!.FieldDeclarations;
        foreach (var field in fields.Where(f => !f.IsStatic))
            if (field.Initializer == null && !CanDefaultInitialize(field.Type))
                throw new TypeCheckException($"{(field.PropertyName == null ? "Field" : "Property")} '{DisplayName(name)}.{field.PropertyName ?? field.Name}' requires an initializer or constructor assignment", line);
    }
}
