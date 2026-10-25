using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private const string DeclaredParameterPrefix = "$declared-parameter$";
    private const string DeclaredShapePrefix = "$declared-shape$";
    private readonly Dictionary<(AstNode Definition, int Index), string> genericContractOrigins = new();

    private string GenericParameterOrigin(AstNode definition, int index)
    {
        if (genericContractOrigins.TryGetValue((definition, index), out var cached)) return cached;
        return genericContractOrigins[(definition, index)] = ComputeGenericParameterOrigin(definition, index);
    }

    private static string ComputeGenericParameterOrigin(AstNode definition, int index)
    {
        string name = definition switch { MethodDeclaration method => method.Name,
            StructDeclaration structure => structure.SourceName, ClassDeclaration cls => cls.SourceName,
            InterfaceDeclaration contract => contract.Name, _ => "" };
        string declaration = string.Join("\0", definition.Span.SourceId?.Path ?? "", definition.GetType().Name,
            definition.Span.Start.ToString(CultureInfo.InvariantCulture), name, index.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(declaration)));
    }

    // A callee's body is checked through its own declaration parameters. Caller
    // constraints and restricted alias views are checked at the call boundary.
    private TypeExpression ClearIncomingGenericContracts(TypeExpression type, bool preserveAliases = false)
    {
        // Published nongeneric field/signature nodes can carry declaration-scope
        // binding state. Preserve them when there is no callee provenance to clear.
        if (preserveAliases && !HasDeclaredParameter(type)) return type;
        TypeExpression Clear(TypeExpression child) => ClearIncomingGenericContracts(child, preserveAliases);
        TypeExpression result = type switch
        {
            PointerTypeExpression pointer => new PointerTypeExpression(Clear(pointer.Inner),
                pointer.IsNullable, pointer.Line, pointer.IsReadOnly),
            ManagedTypeExpression managed => new ManagedTypeExpression(Clear(managed.Inner),
                managed.IsNullable, managed.Line, managed.IsReadOnly,
                managed.Collector == null ? null : Clear(managed.Collector))
                { UsesDefaultCollector = managed.UsesDefaultCollector },
            ReferenceTypeExpression reference => new ReferenceTypeExpression(Clear(reference.Inner), reference.Line),
            ArrayTypeExpression array => new ArrayTypeExpression(Clear(array.ElementType),
                array.Size, array.Line, array.SizeExpression),
            FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(Clear(function.ReturnType),
                function.ParameterTypes.Select(Clear).ToList(), function.IsManaged,
                function.IsNullable, function.Line, function.IsSignature),
            NamedTypeExpression named when named.TypeArguments.Count > 0 => CopyNamed(named,
                named.TypeArguments.Select(Clear).ToList()),
            _ => type.WithGenericParameterView(null)
        };
        result = result.WithGenericParameterView(null).WithQualifiersFrom(type);
        if (type.GenericUse is { } use)
            result = result.WithGenericUse(CopyNamed(use, use.TypeArguments.Select(Clear).ToList()));
        if (preserveAliases && aliasContracts.TryGetValue(type, out var contract)) aliasContracts[result] = contract;
        result.Span = type.Span;
        return result;
    }

    private static NamedTypeExpression CopyNamed(NamedTypeExpression original, List<TypeExpression> arguments)
    {
        var copy = new NamedTypeExpression(original.Name, original.Namespace, original.Line, arguments)
            { Span = original.Span, NameSpan = original.NameSpan };
        return original.IsResolvedNominal ? copy.AsResolvedNominal() : copy;
    }

    // Signature-preview types only: these identities must never become emitted
    // specializations or replace the actual arguments passed to monomorphization.
    private TypeExpression OverloadArgumentType(TypeExpression type)
    {
        if (type.GenericParameterView is { } parameter)
            return NamedTypeExpression.Resolved(DeclaredParameterPrefix + parameter.OriginIdentity, type.Line)
                .WithGenericParameterView(parameter).WithQualifiersFrom(type);
        TypeExpression result = type switch
        {
            PointerTypeExpression pointer => new PointerTypeExpression(OverloadArgumentType(pointer.Inner),
                pointer.IsNullable, pointer.Line, pointer.IsReadOnly),
            ManagedTypeExpression managed => new ManagedTypeExpression(OverloadArgumentType(managed.Inner),
                managed.IsNullable, managed.Line, managed.IsReadOnly,
                managed.Collector == null ? null : OverloadArgumentType(managed.Collector))
                { UsesDefaultCollector = managed.UsesDefaultCollector },
            ReferenceTypeExpression reference => new ReferenceTypeExpression(OverloadArgumentType(reference.Inner), reference.Line),
            ArrayTypeExpression array => new ArrayTypeExpression(OverloadArgumentType(array.ElementType), array.Size, array.Line, array.SizeExpression),
            FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(OverloadArgumentType(function.ReturnType),
                function.ParameterTypes.Select(OverloadArgumentType).ToList(), function.IsManaged,
                function.IsNullable, function.Line, function.IsSignature),
            NamedTypeExpression named => ProjectConstructedType(named),
            _ => type
        };
        result = result.WithQualifiersFrom(type);
        result.Span = type.Span;
        return result;

        TypeExpression ProjectConstructedType(NamedTypeExpression named)
        {
            var origin = named.GenericUse ?? (named.TypeArguments.Count != 0 ? named :
                extensionGenericTypeOrigins.GetValueOrDefault(named.Name));
            if (origin == null || origin.TypeArguments.Count == 0) return named;
            var arguments = origin.TypeArguments.Select(OverloadArgumentType).ToList();
            if (!arguments.Any(HasDeclaredParameter)) return named;
            var projected = CopyNamed(origin, arguments);
            string identity = (origin.Namespace == null ? "" : origin.Namespace.Replace("::", "$") + "$") + origin.Name;
            string shape = identity + "$" + string.Join("$", arguments.Select(GetTypeMangledName));
            return NamedTypeExpression.Resolved(DeclaredShapePrefix + Convert.ToHexString(Encoding.UTF8.GetBytes(shape)), named.Line)
                .WithGenericUse(projected);
        }
    }

    private static bool HasDeclaredParameter(TypeExpression type)
    {
        if (type.GenericParameterView != null) return true;
        return type switch
        {
            PointerTypeExpression pointer => HasDeclaredParameter(pointer.Inner),
            ManagedTypeExpression managed => HasDeclaredParameter(managed.Inner) ||
                managed.Collector != null && HasDeclaredParameter(managed.Collector),
            ReferenceTypeExpression reference => HasDeclaredParameter(reference.Inner),
            ArrayTypeExpression array => HasDeclaredParameter(array.ElementType),
            FunctionPointerTypeExpression function => HasDeclaredParameter(function.ReturnType) || function.ParameterTypes.Any(HasDeclaredParameter),
            NamedTypeExpression named => named.TypeArguments.Any(HasDeclaredParameter) ||
                named.GenericUse != null && named.GenericUse.TypeArguments.Any(HasDeclaredParameter),
            _ => false
        };
    }

    // null means an ordinary concrete argument; false means a declaration
    // parameter does not promise this requirement, regardless of extra interfaces
    // implemented by its current concrete substitution.
    private bool? DeclaredConstraintSatisfied(TypeExpression argument, TypeExpression constraint)
    {
        var view = GetDeclaredParameterView(argument);
        return view == null ? null : Satisfies(view, new HashSet<string>());

        bool Satisfies(GenericParameterContractView declared, HashSet<string> active)
        {
            if (!active.Add(declared.OriginIdentity)) return false;
            try
            {
                foreach (var exposed in declared.Constraints)
                {
                    if (GetDeclaredParameterView(exposed) is { } inherited)
                    {
                        if (Satisfies(inherited, active)) return true;
                        continue;
                    }
                    if (declared.IsOpen
                        ? TypesMatchPublic(OverloadArgumentType(exposed), OverloadArgumentType(constraint))
                        : TypesMatch(OverloadArgumentType(exposed), OverloadArgumentType(constraint))) return true;
                    // IEquatable<A> cannot prove IEquatable<B> merely because A
                    // and B happen to substitute to the same concrete type.
                    if (!declared.IsOpen && (HasDeclaredParameter(exposed) || HasDeclaredParameter(constraint)) && TypesMatch(exposed, constraint)) continue;
                    if (KnownExtensionConstraintSatisfied(exposed, constraint, allowInterfaceIdentity: true)) return true;
                }
                return false;
            }
            finally { active.Remove(declared.OriginIdentity); }
        }
    }

    private static GenericParameterContractView? GetDeclaredParameterView(TypeExpression type)
    {
        if (type.GenericParameterView is { } declared) return declared;
        var inner = type switch { PointerTypeExpression pointer => pointer.Inner,
            ManagedTypeExpression managed => managed.Inner, ReferenceTypeExpression reference => reference.Inner, _ => null };
        return inner == null ? null : GetDeclaredParameterView(inner);
    }
}
