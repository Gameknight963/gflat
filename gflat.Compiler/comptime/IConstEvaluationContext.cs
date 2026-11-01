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
        bool RequiresCopy(TypeExpression type);
        bool IsFreshValue(AstNode expression);
        bool TryGetNativeIntegerOperands(BinaryExpression node, out TypeExpression type);
        bool TryGetIntegerComparisonOperands(BinaryExpression node, out TypeExpression type);
        bool TryGetFloatingOperands(BinaryExpression node, out TypeExpression type);
        bool TryGetConstValue(AstNode node, out ConstValue? value);
        AstNode? GetConstantDeclaration(AstNode expression);
        ConstValue ReadConstantStorage(FieldDeclaration declaration);
        CallExpression? GetPropertyRead(AstNode node);
        TypeChecker.PropertyWrite? GetPropertyWrite(AstNode node);
        // Evaluation receives bound type identities, not source lookup names.
        TypeChecker.StructInfo? GetStruct(string identity);
        TypeChecker.ClassInfo? GetClass(string identity);
        bool IsInterface(string name);
        TypeChecker.InterfaceInfo? GetInterface(string name);
        TypeChecker.EnumInfo? ResolveEnum(NamedTypeExpression named);
        MethodDeclaration? ResolveFunctionForComptime(string name);
        bool TryLookupVariable(string name, out TypeExpression? varType);
        int GetTypeSize(TypeExpression type);
        int GetTypeAlignment(TypeExpression type);
        ConstValue.Reflection ReflectType(TypeExpression type, int line);
        ConstValue.Reflection ReflectExpressionType(AstNode operand, int line);
        bool TryGetReflectionTarget(ReflectExpression node, out ConstValue.Reflection? target);
        ConstValue ReflectionMember(ConstValue.Reflection target, string member, int line);
        string ExtractName(AstNode target);
    }
}
