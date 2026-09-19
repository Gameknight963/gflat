using System.Collections.Generic;
using gflat.ast;

namespace gflat.comptime
{
    public interface IConstEvaluationContext
    {
        ConstructorDeclaration? GetResolvedConstructor(NewExpression node);
        ConstructorDeclaration? GetResolvedBaseConstructor(ConstructorDeclaration node);
        bool TryGetEnumMember(AstNode node, out long value, out TypeExpression? underlyingType);
        bool TryGetConstValueByName(string name, out ConstValue? value);
        TypeExpression ResolveAlias(TypeExpression type);
        TypeExpression GetType(AstNode node);
        TypeChecker.StructInfo? GetStruct(string name);
        TypeChecker.ClassInfo? GetClass(string name);
        bool IsInterface(string name);
        TypeChecker.InterfaceInfo? GetInterface(string name);
        TypeChecker.EnumInfo? ResolveEnum(NamedTypeExpression named);
        MethodDeclaration? ResolveFunctionForComptime(string name);
        bool TryLookupVariable(string name, out TypeExpression? varType);
        int GetTypeSize(TypeExpression type);
        string ExtractName(AstNode target);
    }
}
