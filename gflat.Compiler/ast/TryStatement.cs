using System;
using System.Collections.Generic;
using System.Text;

namespace gflat.ast
{
    public class TryStatement : AstNode
    {
        public BlockStatement TryBlock { get; }
        public List<CatchClause> CatchClauses { get; }

        public TryStatement(BlockStatement tryBlock, List<CatchClause> catchClauses, int line) : base(line)
        {
            TryBlock = tryBlock;
            CatchClauses = catchClauses;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
