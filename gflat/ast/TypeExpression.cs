using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public abstract class TypeExpression : AstNode
    {
        public TypeExpression(int line) : base(line) { }
    }
}
