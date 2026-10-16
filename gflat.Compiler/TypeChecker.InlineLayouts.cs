using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed class LayoutScope(Action exit) : IDisposable { public void Dispose() => exit(); }
    private readonly HashSet<string> activeLayoutQueries = new();
    private readonly List<string> layoutQueryPath = new();
    private readonly HashSet<string> checkedInlineLayouts = new();
    private readonly Dictionary<string, TypeCheckException> invalidInlineLayouts = new();
    private readonly List<(string Type, string? Field)> inlineLayoutPath = new();
    private const int MaximumSpecializationDepth = 64;
    private readonly HashSet<string> activeTypeSpecializations = new();
    private readonly HashSet<string> failedTypeSpecializations = new();
    private readonly HashSet<string> activeFunctionSpecializations = new();
    private readonly HashSet<string> failedFunctionSpecializations = new();

    private IDisposable EnterLayoutQuery(string name, int line)
    {
        CheckFailedTypeSpecialization(name, line);
        CheckInvalidInlineLayout(name);
        if (!activeLayoutQueries.Add(name))
            throw new TypeCheckException("Recursive inline layout query: " + string.Join(" -> ", layoutQueryPath.Append(name).Select(DisplayName)), line);
        layoutQueryPath.Add(name);
        return new LayoutScope(() => { layoutQueryPath.RemoveAt(layoutQueryPath.Count - 1); activeLayoutQueries.Remove(name); });
    }

    private void CheckFailedTypeSpecialization(string name, int line)
    {
        if (failedTypeSpecializations.Contains(name))
            throw new TypeCheckException($"Type specialization exceeds the compiler nesting limit of {MaximumSpecializationDepth}", line);
    }

    private IDisposable EnterTypeSpecialization(string name, int line)
    {
        if (activeTypeSpecializations.Count + activeFunctionSpecializations.Count >= MaximumSpecializationDepth)
        {
            // Recovery revisits partial types registered before the error. Mark
            // this expansion's keys, leaving unrelated specializations available.
            failedTypeSpecializations.UnionWith(activeTypeSpecializations);
            failedFunctionSpecializations.UnionWith(activeFunctionSpecializations);
            failedTypeSpecializations.Add(name);
            CheckFailedTypeSpecialization(name, line);
        }
        activeTypeSpecializations.Add(name);
        return new LayoutScope(() => activeTypeSpecializations.Remove(name));
    }

    private void CheckFailedFunctionSpecialization(string name, int line)
    {
        if (failedFunctionSpecializations.Contains(name))
            throw new TypeCheckException($"Function specialization exceeds the compiler nesting limit of {MaximumSpecializationDepth}", line);
    }

    private IDisposable EnterFunctionSpecialization(string name, int line)
    {
        if (activeTypeSpecializations.Count + activeFunctionSpecializations.Count >= MaximumSpecializationDepth)
        {
            failedTypeSpecializations.UnionWith(activeTypeSpecializations);
            failedFunctionSpecializations.UnionWith(activeFunctionSpecializations);
            failedFunctionSpecializations.Add(name);
            CheckFailedFunctionSpecialization(name, line);
        }
        activeFunctionSpecializations.Add(name);
        return new LayoutScope(() => activeFunctionSpecializations.Remove(name));
    }

    private void CheckInvalidInlineLayout(string name)
    {
        if (invalidInlineLayouts.TryGetValue(name, out var error)) throw error;
    }

    private void ValidateCollectedInlineLayouts()
    {
        foreach (string name in _structs.Keys.Concat(_classes.Keys).ToArray())
            AnalyzeSafely(() => ValidateInlineLayout(name));
    }

    private void ValidateInlineLayout(string name, string? fieldPath = null, SourceSpan? fieldSpan = null)
    {
        CheckFailedTypeSpecialization(name, fieldSpan?.Line ?? SourceContext.Current?.Line ?? 0);
        CheckInvalidInlineLayout(name);
        if (checkedInlineLayouts.Contains(name)) return;
        int cycle = inlineLayoutPath.FindIndex(entry => entry.Type == name);
        if (cycle >= 0)
        {
            var path = inlineLayoutPath.Skip(cycle + 1).Select(entry => entry.Field!).Append(fieldPath!).Append(DisplayName(name));
            var error = new TypeCheckException("Inline value layout is recursive: " + string.Join(" -> ", path) + ". Store a pointer or managed reference to break the cycle.", fieldSpan ?? SourceContext.Current ?? default);
            // All containing values embed this impossible layout. Remember them
            // so editor recovery cannot restart structural copy traversal later.
            foreach (var entry in inlineLayoutPath) invalidInlineLayouts[entry.Type] = error;
            throw error;
        }
        inlineLayoutPath.Add((name, fieldPath));
        try
        {
            if (GetStruct(name) is { } str)
                foreach (var field in str.Fields)
                    VisitField(field.Name, field.Type, str.FieldDeclarationsByName.GetValueOrDefault(field.Name));
            else if (GetClass(name) is { } cls)
            {
                if (cls.BaseClass != null) ValidateInlineLayout(cls.BaseClass, DisplayName(name) + " : " + DisplayName(cls.BaseClass), fieldSpan);
                foreach (var field in cls.Fields.Where(field => field.DeclaringClass == name))
                    VisitField(field.Name, field.Type, cls.FieldDeclarationsByName.GetValueOrDefault(field.Name));
            }
            checkedInlineLayouts.Add(name);
        }
        catch (TypeCheckException error) when (invalidInlineLayouts.ContainsValue(error))
        {
            invalidInlineLayouts[name] = error;
            throw;
        }
        finally { inlineLayoutPath.RemoveAt(inlineLayoutPath.Count - 1); }

        void VisitField(string member, TypeExpression type, FieldDeclaration? declaration)
        {
            using var context = EnterFieldLayoutContext(name, member);
            SourceSpan span = declaration?.Span ?? fieldSpan ?? SourceContext.Current ?? default;
            using var source = SourceContext.Enter(span);
            VisitValue(type, DisplayName(name) + "." + member, span);
        }
        void VisitValue(TypeExpression type, string path, SourceSpan span)
        {
            if (type is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression or ReferenceTypeExpression) return;
            if (type is ArrayTypeExpression array) { VisitValue(array.ElementType, path, span); return; }
            TypeExpression resolved = ResolveFieldLayoutType(type);
            if (resolved is PointerTypeExpression or ManagedTypeExpression or FunctionPointerTypeExpression or ReferenceTypeExpression) return;
            if (resolved is ArrayTypeExpression resolvedArray) { VisitValue(resolvedArray.ElementType, path, span); return; }
            if (resolved is NamedTypeExpression named && (GetStruct(named.Name) != null || GetClass(named.Name) != null))
                ValidateInlineLayout(named.Name, path, span);
        }
    }
}
