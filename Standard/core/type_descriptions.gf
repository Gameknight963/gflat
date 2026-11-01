namespace Standard;

// Returns the shared descriptor graph for T. The data has static lifetime.
public const immutable(TypeInfo)* Describe<T>() nothrow
    => &detail::TypeDescription<T>::Value;

namespace detail
{
    const nuint DescriptionParameterCount<T>() nothrow
    {
        static if (reflect(T).IsFunctionPointer || reflect(T).IsFunctionSignature)
        {
            return reflect(T).ParameterCount;
        }
        return 0;
    }

    const FieldInfo[reflect(T).Fields.Length] BuildFieldDescriptions<T>() nothrow
    {
        FieldInfo[reflect(T).Fields.Length] fields;
        nuint index = 0;
        static foreach (reflection field in reflect(T).Fields)
        {
            fields[index] = new FieldInfo(field.Name, Describe<[:field.Type:]>(),
                field.Offset, field.IsPublic, field.IsPrivate);
            index++;
        }
        return fields;
    }

    const immutable(TypeInfo)*[DescriptionParameterCount<T>()] BuildParameterDescriptions<T>() nothrow
    {
        immutable(TypeInfo)*[DescriptionParameterCount<T>()] parameters;
        nuint index = 0;
        static if (reflect(T).IsFunctionPointer || reflect(T).IsFunctionSignature)
        {
            static foreach (reflection parameter in reflect(T).ParameterTypes)
            {
                parameters[index] = Describe<[:parameter:]>();
                index++;
            }
        }
        return parameters;
    }

    const TypeInfo BuildTypeDescription<T>() nothrow
    {
        TypeInfo result = new();
        result.Name = reflect(T).QualifiedName;
        result.HasLayout = reflect(T).HasLayout;
        result.Size = reflect(T).Size;
        result.Alignment = reflect(T).Alignment;
        result.IsPointer = reflect(T).IsPointer;
        result.IsManaged = reflect(T).IsManaged;
        result.IsNullable = reflect(T).IsNullable;
        result.IsReadOnly = reflect(T).IsReadOnly;
        result.IsImmutable = reflect(T).IsImmutable;
        result.HasDestructor = reflect(T).HasDestructor;
        result.HasFixedLength = reflect(T).HasFixedLength;
        result.FieldCount = reflect(T).Fields.Length;
        result.ParameterCount = DescriptionParameterCount<T>();

        static if (reflect(T).IsClass) result.Kind = TypeKind::Class;
        static if (reflect(T).IsStruct) result.Kind = TypeKind::Struct;
        static if (reflect(T).IsEnum) result.Kind = TypeKind::Enum;
        static if (reflect(T).IsInterface) result.Kind = TypeKind::Interface;
        static if (reflect(T).IsPointer) result.Kind = TypeKind::Pointer;
        static if (reflect(T).IsManaged) result.Kind = TypeKind::ManagedPointer;
        static if (reflect(T).IsArray) result.Kind = TypeKind::Array;
        static if (reflect(T).IsFunctionPointer) result.Kind = TypeKind::FunctionPointer;
        static if (reflect(T).IsReference) result.Kind = TypeKind::Reference;
        static if (reflect(T).IsFunctionSignature) result.Kind = TypeKind::FunctionSignature;

        static if (reflect(T).HasElementType)
        {
            result.ElementType = Describe<[:reflect(T).ElementType:]>();
        }
        static if (reflect(T).HasFixedLength) result.Length = reflect(T).Length;
        static if (reflect(T).IsFunctionPointer || reflect(T).IsFunctionSignature)
        {
            result.ReturnType = Describe<[:reflect(T).ReturnType:]>();
        }
        static if (DescriptionParameterCount<T>() != 0)
        {
            result.ParameterTypes = TypeDescription<T>::Parameters;
        }
        static if (reflect(T).Fields.Length != 0)
        {
            result.Fields = TypeDescription<T>::Fields;
        }
        static if (reflect(T).Bases.Length != 0)
        {
            result.BaseType = Describe<[:reflect(T).Bases[0]:]>();
        }
        static if (reflect(T).IsManaged)
        {
            result.CollectorType = Describe<[:reflect(T).CollectorType:]>();
        }
        return result;
    }

    static class TypeDescription<T>
    {
        public const immutable(TypeInfo) Value = BuildTypeDescription<T>();
        public const immutable(FieldInfo)[reflect(T).Fields.Length] Fields = BuildFieldDescriptions<T>();
        public const readonly(immutable(TypeInfo)*)[DescriptionParameterCount<T>()] Parameters = BuildParameterDescriptions<T>();
    }
}
