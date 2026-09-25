using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public bool CanDefaultInitialize(TypeExpression type) => CanDefaultInitialize(type, new HashSet<string>());

    private bool CanDefaultInitialize(TypeExpression type, HashSet<string> visiting)
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
            if (GetStruct(named.Name) is StructInfo str)
                return str.Constructors.Count == 0 && str.Fields.All(f => CanDefaultInitialize(f.Type, visiting));
            if (GetClass(named.Name) is ClassInfo cls)
                return !cls.IsAbstract && cls.Constructors.Count == 0 &&
                    (cls.BaseClass == null || CanDefaultInitialize(new NamedTypeExpression(cls.BaseClass, null, type.Line), visiting)) &&
                    cls.Fields.All(f => CanDefaultInitialize(f.Type, visiting));
            return !IsInterface(named);
        }
        finally { visiting.Remove(named.Name); }
    }

    private void ValidateImplicitConstruction(string name, int line)
    {
        IEnumerable<FieldDeclaration> fields;
        if (GetClass(name) is ClassInfo cls)
        {
            fields = cls.FieldDeclarations;
            if (cls.BaseClass != null && GetClass(cls.BaseClass) is ClassInfo parent)
            {
                if (parent.Constructors.Count == 0) ValidateImplicitConstruction(parent.Name, line);
                else if (!parent.Constructors.Any(c => c.Parameters.Count == 0))
                    throw new TypeCheckException($"Base class '{parent.Name}' requires an explicit constructor call", line);
            }
        }
        else fields = GetStruct(name)!.FieldDeclarations;
        foreach (var field in fields)
            if (field.Initializer == null && !CanDefaultInitialize(field.Type))
                throw new TypeCheckException($"Field '{name}.{field.Name}' requires an initializer or constructor assignment", line);
    }
}
