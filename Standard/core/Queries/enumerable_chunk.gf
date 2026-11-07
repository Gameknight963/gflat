namespace Standard::Queries;

public Enumerable<Group<T>, ChunkSource<T, S, E>, ChunkSource<T, S, E>::Enumerator>
    Chunk<T, S, E>(Enumerable<T, S, E> source, nuint size)
    => source.Chunk(size);

extension Enumerable<T, S, E>
{
    public Enumerable<Group<T>, ChunkSource<T, S, E>, ChunkSource<T, S, E>::Enumerator> Chunk(nuint size)
        => new Enumerable<Group<T>, ChunkSource<T, S, E>, ChunkSource<T, S, E>::Enumerator>(
            new ChunkSource<T, S, E>((*this).SourceView(), size));
}
