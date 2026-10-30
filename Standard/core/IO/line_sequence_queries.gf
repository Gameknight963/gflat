using Standard::Text;
using Standard::Queries;
namespace Standard::IO;

extension LineSequence
{
    public Enumerable<String, LineSequence, LineSequence::Enumerator> AsEnumerable()
        => new Enumerable<String, LineSequence, LineSequence::Enumerator>(*this);
}
