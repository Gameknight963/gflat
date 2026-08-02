using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class BlockStatement : AstNode
    {
        public List<AstNode> Statements { get; }

        public BlockStatement(List<AstNode> statements, int line) : base(line) => Statements = statements;
        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
