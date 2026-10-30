using Standard::Queries;
namespace Standard::IO;

extension ByteSequence
{
    public Enumerable<byte, ByteSequence, ByteSequence::Enumerator> AsEnumerable()
        => new Enumerable<byte, ByteSequence, ByteSequence::Enumerator>(*this);
}
