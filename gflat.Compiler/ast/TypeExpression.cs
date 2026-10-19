using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class TypeExpression : AstNode
    {
        // Qualification of this value's storage, distinct from a pointer's pointee.
        public bool IsReadOnlyValue { get; private set; }
        // Per-use constraints survive monomorphization; they are not part of type identity.
        internal IReadOnlyList<TypeExpression> InterfaceConstraints { get; private set; } = Array.Empty<TypeExpression>();
        internal NamedTypeExpression? GenericUse { get; private set; }
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
            copy.IsReadOnlyValue = value;
            return copy;
        }
        public TypeExpression(int line) : base(line) { }
    }
}
