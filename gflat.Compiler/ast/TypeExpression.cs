using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    // A compile-time view of one declaration parameter, never nominal identity.
    internal sealed record GenericParameterContractView(string OriginIdentity, AstNode DeclaringDefinition,
        string ParameterName, TypeExpression ActualType, IReadOnlyList<TypeExpression> Constraints);

    public abstract class TypeExpression : AstNode
    {
        // Qualification of this value's storage, distinct from a pointer's pointee.
        public bool IsReadOnlyValue { get; private set; }
        public bool IsImmutableValue { get; private set; }
        // Per-use constraints survive monomorphization; they are not part of type identity.
        internal IReadOnlyList<TypeExpression> InterfaceConstraints { get; private set; } = Array.Empty<TypeExpression>();
        internal NamedTypeExpression? GenericUse { get; private set; }
        internal GenericParameterContractView? GenericParameterView { get; private set; }
        internal TypeExpression WithGenericParameterView(GenericParameterContractView? view)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.GenericParameterView = view;
            return copy;
        }
        internal TypeExpression WithGenericUse(NamedTypeExpression use)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.GenericUse = use;
            return copy;
        }
        internal TypeExpression WithInterfaceConstraints(IReadOnlyList<TypeExpression> constraints)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.InterfaceConstraints = constraints;
            return copy;
        }
        public TypeExpression WithReadOnlyValue(bool value)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.IsReadOnlyValue = value || copy.IsImmutableValue;
            return copy;
        }
        public TypeExpression WithImmutableValue(bool value)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.IsImmutableValue = value;
            if (value) copy.IsReadOnlyValue = true;
            return copy;
        }
        public TypeExpression WithoutValueProtection()
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.IsReadOnlyValue = false;
            copy.IsImmutableValue = false;
            return copy;
        }
        public TypeExpression WithQualifiersFrom(TypeExpression source) => source.IsImmutableValue
            ? TypeQualifiers.Immutable(this) : source.IsReadOnlyValue ? TypeQualifiers.ReadOnly(this) : this;
        public TypeExpression(int line) : base(line) { }
    }
}
