using std::Numerics;
namespace std::Hashing;

// Incremental SHA-256 over counted bytes. Specification: RFC 6234 sections 4-6.
// Inline state is independently copied; GetHash finalizes only a snapshot.
public struct SHA256
{
    private uint[8] state;
    private byte[64] block;
    private nuint buffered;
    private ulong length;
    private static const ulong MaximumBytes = 2305843009213693951ul;
    private static const uint[64] RoundConstants = [
        0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u,
        0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
        0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u,
        0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
        0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu,
        0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
        0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u,
        0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
        0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u,
        0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
        0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u,
        0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
        0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u,
        0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
        0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u,
        0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
    ];

    public SHA256() nothrow { this.Reset(); }

    public void Reset() nothrow
    {
        state = [0x6a09e667u, 0xbb67ae85u, 0x3c6ef372u, 0xa54ff53au,
            0x510e527fu, 0x9b05688cu, 0x1f83d9abu, 0x5be0cd19u];
        for (nuint i = 0; i < 64; i++) block[i] = (byte)0;
        buffered = 0;
        length = 0ul;
    }

    // False rejects the entire append before reading input or changing state.
    public bool TryAppend(readonly(char)* data, nuint count) nothrow
    {
        if ((ulong)count > MaximumBytes - length) return false;
        nuint index = 0;
        if (buffered != 0)
        {
            while (index < count && buffered < 64)
            {
                block[buffered] = (byte)data[index];
                buffered++; index++;
            }
            if (buffered == 64)
            {
                this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
                buffered = 0;
            }
        }
        while (count - index >= 64)
        {
            this.ProcessBlock(data + index);
            index += 64;
        }
        while (index < count)
        {
            block[buffered] = (byte)data[index];
            buffered++; index++;
        }
        length += (ulong)count;
        return true;
    }

    private void ProcessBlock(readonly(char)* data) nothrow
    {
        uint[64] schedule;
        for (nuint i = 0; i < 16; i++)
        {
            nuint offset = i * 4;
            schedule[i] = ((uint)(byte)data[offset] << 24) |
                ((uint)(byte)data[offset + 1] << 16) |
                ((uint)(byte)data[offset + 2] << 8) | (uint)(byte)data[offset + 3];
        }
        for (nuint i = 16; i < 64; i++)
        {
            uint earlier = schedule[i - 15];
            uint later = schedule[i - 2];
            uint first = BitOperations::RotateRight(earlier, 7) ^ BitOperations::RotateRight(earlier, 18) ^ (earlier >> 3);
            uint second = BitOperations::RotateRight(later, 17) ^ BitOperations::RotateRight(later, 19) ^ (later >> 10);
            schedule[i] = schedule[i - 16] + first + schedule[i - 7] + second;
        }
        uint a = state[0]; uint b = state[1]; uint c = state[2]; uint d = state[3];
        uint e = state[4]; uint f = state[5]; uint g = state[6]; uint h = state[7];
        for (nuint i = 0; i < 64; i++)
        {
            uint upper = BitOperations::RotateRight(e, 6) ^ BitOperations::RotateRight(e, 11) ^ BitOperations::RotateRight(e, 25);
            uint choose = (e & f) ^ ((e ^ 0xffffffffu) & g);
            uint first = h + upper + choose + RoundConstants[i] + schedule[i];
            uint lower = BitOperations::RotateRight(a, 2) ^ BitOperations::RotateRight(a, 13) ^ BitOperations::RotateRight(a, 22);
            uint majority = (a & b) ^ (a & c) ^ (b & c);
            uint second = lower + majority;
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
        if (buffered > 56)
        {
            while (buffered < 64) { block[buffered] = (byte)0; buffered++; }
            this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
            buffered = 0;
        }
        while (buffered < 56) { block[buffered] = (byte)0; buffered++; }
        ulong bits = length << 3;
        for (int i = 0; i < 8; i++) block[56 + i] = (byte)(bits >> ((7 - i) * 8));
        this.ProcessBlock(((byte*)block).reinterpret<readonly(char)*>());
    }

    public immutable byte[32] GetHash() nothrow
    {
        SHA256 snapshot = *this;
        snapshot.Pad();
        byte[32] digest;
        for (nuint i = 0; i < 8; i++)
        {
            uint word = snapshot.state[i];
            for (int part = 0; part < 4; part++)
                digest[i * 4 + (nuint)part] = (byte)(word >> (24 - part * 8));
        }
        return digest;
    }
}
