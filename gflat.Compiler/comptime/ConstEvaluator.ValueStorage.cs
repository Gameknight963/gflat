using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    // Values own their inline fields/elements. References retain their original
    // target, including pointers embedded inside copied values.
    private ConstValue CopyTransferredValue(ConstValue value, TypeExpression target)
    {
        target = _context.ResolveAlias(target);
        if (target is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression or ReferenceTypeExpression)
            return value;
        if (target is ArrayTypeExpression array && value is ConstValue.Array elements)
            return new ConstValue.Array(elements.Elements.Select(element => CopyTransferredValue(element, array.ElementType)).ToList());
        if (value is ConstValue.Struct structure)
            return new ConstValue.Struct(structure.StructName, CopyFields(structure.Fields, structure.StructName));
        if (value is ConstValue.ClassInstance instance)
            return new ConstValue.ClassInstance(instance.ClassName, CopyFields(instance.Fields, instance.ClassName));
        return value;
    }

    private Dictionary<string, ConstValue> CopyFields(Dictionary<string, ConstValue> fields, string owner)
    {
        var types = InstanceFieldTypes(owner);
        return fields.ToDictionary(field => field.Key, field => types.TryGetValue(field.Key, out var type)
            ? CopyTransferredValue(field.Value, type) : field.Value);
    }

    private Dictionary<string, TypeExpression> InstanceFieldTypes(string owner)
        => (_context.GetStruct(owner)?.Fields.Select(field => (field.Name, field.Type)) ??
            _context.GetClass(owner)?.Fields.Select(field => (field.Name, field.Type)) ?? [])
            .ToDictionary(field => field.Name, field => field.Type);

    // Assignment writes to an existing location. Keep its aggregate containers
    // so a previously captured pointer still observes this storage after a write.
    private ConstValue OverwriteValueStorage(ConstValue? previous, ConstValue value, TypeExpression target)
    {
        target = _context.ResolveAlias(target);
        if (target is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression or ReferenceTypeExpression)
            return value;
        if (target is ArrayTypeExpression array && previous is ConstValue.Array oldArray && value is ConstValue.Array newArray &&
            oldArray.Elements.Count == newArray.Elements.Count)
        {
            for (int i = 0; i < oldArray.Elements.Count; i++)
                oldArray.Elements[i] = OverwriteValueStorage(oldArray.Elements[i], newArray.Elements[i], array.ElementType);
            return oldArray;
        }
        if (previous is ConstValue.Struct oldStruct && value is ConstValue.Struct newStruct && oldStruct.StructName == newStruct.StructName)
        {
            OverwriteFields(oldStruct.Fields, newStruct.Fields, oldStruct.StructName);
            return oldStruct;
        }
        if (previous is ConstValue.ClassInstance oldClass && value is ConstValue.ClassInstance newClass && oldClass.ClassName == newClass.ClassName)
        {
            OverwriteFields(oldClass.Fields, newClass.Fields, oldClass.ClassName);
            return oldClass;
        }
        return value;
    }

    private void OverwriteFields(Dictionary<string, ConstValue> previous, Dictionary<string, ConstValue> value, string owner)
    {
        var types = InstanceFieldTypes(owner);
        foreach (var field in value)
            previous[field.Key] = types.TryGetValue(field.Key, out var type)
                ? OverwriteValueStorage(previous.GetValueOrDefault(field.Key), field.Value, type) : field.Value;
    }
}
