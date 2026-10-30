using Standard::Numerics;
namespace Standard::Hashing;

// Scalar algorithm: RFC 6234 sections 4.2, 5.2, 6.3 and 6.4.
/// Incremental SHA-512 over counted bytes with independently copied inline state.
/// GetHash finalizes a snapshot, allowing repeated digests and further appends.
public struct SHA512
{
    private ulong[8] state;
    private byte[128] block;
    private nuint buffered;
    private ulong lengthLow;
    private ulong lengthHigh;
    private static const ulong MaximumHigh = 0x1ffffffffffffffful;
    private static const ulong[80] RoundConstants = [
        0x428a2f98d728ae22ul, 0x7137449123ef65cdul, 0xb5c0fbcfec4d3b2ful, 0xe9b5dba58189dbbcul,
        0x3956c25bf348b538ul, 0x59f111f1b605d019ul, 0x923f82a4af194f9bul, 0xab1c5ed5da6d8118ul,
        0xd807aa98a3030242ul, 0x12835b0145706fbeul, 0x243185be4ee4b28cul, 0x550c7dc3d5ffb4e2ul,
        0x72be5d74f27b896ful, 0x80deb1fe3b1696b1ul, 0x9bdc06a725c71235ul, 0xc19bf174cf692694ul,
        0xe49b69c19ef14ad2ul, 0xefbe4786384f25e3ul, 0x0fc19dc68b8cd5b5ul, 0x240ca1cc77ac9c65ul,
        0x2de92c6f592b0275ul, 0x4a7484aa6ea6e483ul, 0x5cb0a9dcbd41fbd4ul, 0x76f988da831153b5ul,
        0x983e5152ee66dfabul, 0xa831c66d2db43210ul, 0xb00327c898fb213ful, 0xbf597fc7beef0ee4ul,
        0xc6e00bf33da88fc2ul, 0xd5a79147930aa725ul, 0x06ca6351e003826ful, 0x142929670a0e6e70ul,
        0x27b70a8546d22ffcul, 0x2e1b21385c26c926ul, 0x4d2c6dfc5ac42aedul, 0x53380d139d95b3dful,
        0x650a73548baf63deul, 0x766a0abb3c77b2a8ul, 0x81c2c92e47edaee6ul, 0x92722c851482353bul,
        0xa2bfe8a14cf10364ul, 0xa81a664bbc423001ul, 0xc24b8b70d0f89791ul, 0xc76c51a30654be30ul,
        0xd192e819d6ef5218ul, 0xd69906245565a910ul, 0xf40e35855771202aul, 0x106aa07032bbd1b8ul,
        0x19a4c116b8d2d0c8ul, 0x1e376c085141ab53ul, 0x2748774cdf8eeb99ul, 0x34b0bcb5e19b48a8ul,
        0x391c0cb3c5c95a63ul, 0x4ed8aa4ae3418acbul, 0x5b9cca4f7763e373ul, 0x682e6ff3d6b2b8a3ul,
        0x748f82ee5defb2fcul, 0x78a5636f43172f60ul, 0x84c87814a1f0ab72ul, 0x8cc702081a6439ecul,
        0x90befffa23631e28ul, 0xa4506cebde82bde9ul, 0xbef9a3f7b2c67915ul, 0xc67178f2e372532bul,
        0xca273eceea26619cul, 0xd186b8c721c0c207ul, 0xeada7dd6cde0eb1eul, 0xf57d4f7fee6ed178ul,
        0x06f067aa72176fbaul, 0x0a637dc5a2c898a6ul, 0x113f9804bef90daeul, 0x1b710b35131c471bul,
        0x28db77f523047d84ul, 0x32caab7b40c72493ul, 0x3c9ebe0a15c9bebcul, 0x431d67c49c100d4cul,
        0x4cc5d4becb3e42b6ul, 0x597f299cfc657e2aul, 0x5fcb6fab3ad6faecul, 0x6c44198c4a475817ul
    ];

    public SHA512() nothrow { this.Reset(); }

    public void Reset() nothrow
    {
        state = [0x6a09e667f3bcc908ul, 0xbb67ae8584caa73bul, 0x3c6ef372fe94f82bul, 0xa54ff53a5f1d36f1ul,
            0x510e527fade682d1ul, 0x9b05688c2b3e6c1ful, 0x1f83d9abfb41bd6bul, 0x5be0cd19137e2179ul];
        for (nuint i = 0; i < 128; i++) block[i] = (byte)0;
        buffered = 0;
        lengthLow = 0ul;
        lengthHigh = 0ul;
    }

    /// Appends counted bytes without retaining input. Zero count reads no input.
    /// Input must remain readable and stable during the call, disjoint from this state.
    /// False rejects an excessive total length before reading input or changing state.
    public bool TryAppend(readonly(char)* data, nuint count) nothrow
    {
        // Two words count bytes; shifting into the 128-bit bit count reserves
        // three high bits. Each pointer-sized append can carry at most once.
        ulong nextLow = lengthLow + (ulong)count;
        ulong nextHigh = lengthHigh;
        if (nextLow < lengthLow)
        {
            if (nextHigh == MaximumHigh) return false;
            nextHigh++;
        }
        nuint index = 0;
        if (buffered != 0)
        {
            while (index < count && buffered < 128)
            {
                block[buffered] = (byte)data[index];
                buffered++; index++;
            }
            if (buffered == 128)
            {
                this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
                buffered = 0;
            }
        }
        while (count - index >= 128)
        {
            this.ProcessBlock(data + index);
            index += 128;
        }
        while (index < count)
        {
            block[buffered] = (byte)data[index];
            buffered++; index++;
        }
        lengthLow = nextLow;
        lengthHigh = nextHigh;
        return true;
    }

    private void ProcessBlock(readonly(char)* data) nothrow
    {
        ulong[80] schedule;
        for (nuint i = 0; i < 16; i++)
        {
            ulong word = 0ul;
            for (nuint part = 0; part < 8; part++) word = (word << 8) | (ulong)(byte)data[i * 8 + part];
            schedule[i] = word;
        }
        for (nuint i = 16; i < 80; i++)
        {
            ulong earlier = schedule[i - 15];
            ulong later = schedule[i - 2];
            ulong first = BitOperations::RotateRight(earlier, 1) ^ BitOperations::RotateRight(earlier, 8) ^ (earlier >> 7);
            ulong second = BitOperations::RotateRight(later, 19) ^ BitOperations::RotateRight(later, 61) ^ (later >> 6);
            schedule[i] = schedule[i - 16] + first + schedule[i - 7] + second;
        }
        ulong a = state[0]; ulong b = state[1]; ulong c = state[2]; ulong d = state[3];
        ulong e = state[4]; ulong f = state[5]; ulong g = state[6]; ulong h = state[7];
        for (nuint i = 0; i < 80; i++)
        {
            ulong upper = BitOperations::RotateRight(e, 14) ^ BitOperations::RotateRight(e, 18) ^ BitOperations::RotateRight(e, 41);
            ulong choose = (e & f) ^ ((e ^ 0xfffffffffffffffful) & g);
            ulong first = h + upper + choose + RoundConstants[i] + schedule[i];
            ulong lower = BitOperations::RotateRight(a, 28) ^ BitOperations::RotateRight(a, 34) ^ BitOperations::RotateRight(a, 39);
            ulong majority = (a & b) ^ (a & c) ^ (b & c);
            ulong second = lower + majority;
            h = g; g = f; f = e; e = d + first;
            d = c; c = b; b = a; a = first + second;
        }
        state[0] += a; state[1] += b; state[2] += c; state[3] += d;
        state[4] += e; state[5] += f; state[6] += g; state[7] += h;
    }

    private void Pad() nothrow
    {
        block[buffered] = (byte)128;
        buffered++;
        if (buffered > 112)
        {
            while (buffered < 128) { block[buffered] = (byte)0; buffered++; }
            this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
            buffered = 0;
        }
        while (buffered < 112) { block[buffered] = (byte)0; buffered++; }
        ulong bitsHigh = (lengthHigh << 3) | (lengthLow >> 61);
        ulong bitsLow = lengthLow << 3;
        for (int i = 0; i < 8; i++)
        {
            block[112 + i] = (byte)(bitsHigh >> ((7 - i) * 8));
            block[120 + i] = (byte)(bitsLow >> ((7 - i) * 8));
        }
        this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
    }

    /// Returns an independent 64-byte digest without consuming or resetting this state.
    public immutable byte[64] GetHash() nothrow
    {
        SHA512 snapshot = *this;
        snapshot.Pad();
        byte[64] digest;
        for (nuint i = 0; i < 8; i++)
        {
            ulong word = snapshot.state[i];
            for (int part = 0; part < 8; part++)
                digest[i * 8 + (nuint)part] = (byte)(word >> (56 - part * 8));
        }
        return digest;
    }
}
