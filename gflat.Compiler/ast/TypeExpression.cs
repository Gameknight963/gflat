using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class TypeExpression : AstNode
    {
        // Qualification of this value's storage, distinct from a pointer's pointee.
        public bool IsReadOnlyValue { get; private set; }
        public TypeExpression WithReadOnlyValue(bool value)
        {
            var copy = (TypeExpression)MemberwiseClone();
            copy.IsReadOnlyValue = value;
            return copy;
        }
        public TypeExpression(int line) : base(line) { }
    }
}
