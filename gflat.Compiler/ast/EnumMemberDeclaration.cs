namespace gflat.ast
{
    public class EnumMemberDeclaration : AstNode
    {
        public string Name { get; }
        public AstNode? Value { get; }

        public EnumMemberDeclaration(string name, AstNode? value, int line) : base(line)
        {
            Name = name;
            Value = value;
        }

        public override void Accept(IVisitor visitor) => visitor.Visit(this);
    }
}
