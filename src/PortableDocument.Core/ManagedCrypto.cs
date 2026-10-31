using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace PortableDocument.Core;

/// <summary>
/// Pure-managed MD5, SHA-1/2, AES and RC4 for runtimes where System.Security.Cryptography is unavailable (browser WASM).
/// No reflection, no native calls: AOT and trimming safe. Round constants are derived from first principles at first use.
/// AES uses table lookups (not cache-timing hardened); every secret comparison uses <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
public static class ManagedCrypto
{
    /// <summary>MD5 digest (RFC 1321).</summary>
    public static byte[] Md5(ReadOnlySpan<byte> data)
    {
        uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
        var k = Md5Tables.K; var s = Md5Tables.S;
        var msg = Pad(data, 64, bigEndian: false, lengthBytes: 8);
        var m = new uint[16];
        for (var off = 0; off < msg.Length; off += 64)
        {
            for (var i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt32LittleEndian(msg.AsSpan(off + i * 4));
            uint a = a0, b = b0, c = c0, d = d0;
            for (var i = 0; i < 64; i++)
            {
                uint f; int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) & 15; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) & 15; }
                else { f = c ^ (b | ~d); g = (7 * i) & 15; }
                f += a + k[i] + m[g];
                a = d; d = c; c = b;
                b += BitOperations.RotateLeft(f, s[i]);
            }
            a0 += a; b0 += b; c0 += c; d0 += d;
        }
        var o = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(o, a0); BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(4), b0);
        BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(8), c0); BinaryPrimitives.WriteUInt32LittleEndian(o.AsSpan(12), d0);
        return o;
    }

    /// <summary>SHA-1 digest (FIPS 180-4).</summary>
    public static byte[] Sha1(ReadOnlySpan<byte> data)
    {
        uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;
        var msg = Pad(data, 64, bigEndian: true, lengthBytes: 8);
        var w = new uint[80];
        for (var off = 0; off < msg.Length; off += 64)
        {
            for (var i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(off + i * 4));
            for (var i = 16; i < 80; i++) w[i] = BitOperations.RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (var i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                var t = BitOperations.RotateLeft(a, 5) + f + e + k + w[i];
                e = d; d = c; c = BitOperations.RotateLeft(b, 30); b = a; a = t;
            }
            h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
        }
        var o = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(o, h0); BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(4), h1);
        BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(8), h2); BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(12), h3);
        BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(16), h4);
        return o;
    }

    /// <summary>SHA-256 digest (FIPS 180-4).</summary>
    public static byte[] Sha256(ReadOnlySpan<byte> data)
    {
        var h = (uint[])Sha2Tables.H256.Clone();
        var k = Sha2Tables.K256;
        var msg = Pad(data, 64, bigEndian: true, lengthBytes: 8);
        var w = new uint[64];
        for (var off = 0; off < msg.Length; off += 64)
        {
            for (var i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(off + i * 4));
            for (var i = 16; i < 64; i++)
            {
                var s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
                var s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (var i = 0; i < 64; i++)
            {
                var t1 = hh + (BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25)) + ((e & f) ^ (~e & g)) + k[i] + w[i];
                var t2 = (BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }
        var o = new byte[32];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(i * 4), h[i]);
        return o;
    }

    /// <summary>SHA-384 digest.</summary>
    public static byte[] Sha384(ReadOnlySpan<byte> data) => Sha512Core(data, Sha2Tables.H384, 48);

    /// <summary>SHA-512 digest.</summary>
    public static byte[] Sha512(ReadOnlySpan<byte> data) => Sha512Core(data, Sha2Tables.H512, 64);

    static byte[] Sha512Core(ReadOnlySpan<byte> data, ulong[] init, int outLen)
    {
        var h = (ulong[])init.Clone();
        var k = Sha2Tables.K512;
        var msg = Pad(data, 128, bigEndian: true, lengthBytes: 16);
        var w = new ulong[80];
        for (var off = 0; off < msg.Length; off += 128)
        {
            for (var i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt64BigEndian(msg.AsSpan(off + i * 8));
            for (var i = 16; i < 80; i++)
            {
                var s0 = BitOperations.RotateRight(w[i - 15], 1) ^ BitOperations.RotateRight(w[i - 15], 8) ^ (w[i - 15] >> 7);
                var s1 = BitOperations.RotateRight(w[i - 2], 19) ^ BitOperations.RotateRight(w[i - 2], 61) ^ (w[i - 2] >> 6);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }
            ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (var i = 0; i < 80; i++)
            {
                var t1 = hh + (BitOperations.RotateRight(e, 14) ^ BitOperations.RotateRight(e, 18) ^ BitOperations.RotateRight(e, 41)) + ((e & f) ^ (~e & g)) + k[i] + w[i];
                var t2 = (BitOperations.RotateRight(a, 28) ^ BitOperations.RotateRight(a, 34) ^ BitOperations.RotateRight(a, 39)) + ((a & b) ^ (a & c) ^ (b & c));
                hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
        }
        var full = new byte[64];
        for (var i = 0; i < 8; i++) BinaryPrimitives.WriteUInt64BigEndian(full.AsSpan(i * 8), h[i]);
        return outLen == 64 ? full : full[..outLen];
    }

    // Merkle–Damgård padding: 0x80, zeros, then the bit length (little-endian for MD5, big-endian otherwise).
    static byte[] Pad(ReadOnlySpan<byte> data, int block, bool bigEndian, int lengthBytes)
    {
        var total = (data.Length + 1 + lengthBytes + block - 1) / block * block;
        var msg = new byte[total];
        data.CopyTo(msg);
        msg[data.Length] = 0x80;
        var bits = (ulong)data.Length * 8;
        if (bigEndian) BinaryPrimitives.WriteUInt64BigEndian(msg.AsSpan(total - 8), bits);
        else BinaryPrimitives.WriteUInt64LittleEndian(msg.AsSpan(total - 8), bits);
        return msg;
    }

    /// <summary>HMAC (RFC 2104) over any of the managed hashes.</summary>
    public static byte[] Hmac(Func<byte[], byte[]> hash, int blockSize, byte[] key, byte[] data)
    {
        if (key.Length > blockSize) key = hash(key);
        var ipad = new byte[blockSize + data.Length]; var opad = new byte[blockSize];
        for (var i = 0; i < blockSize; i++) { var kb = i < key.Length ? key[i] : (byte)0; ipad[i] = (byte)(kb ^ 0x36); opad[i] = (byte)(kb ^ 0x5c); }
        data.CopyTo(ipad, blockSize);
        var inner = hash(ipad);
        return hash([.. opad, .. inner]);
    }

    /// <summary>HMAC-SHA-256.</summary>
    public static byte[] HmacSha256(byte[] key, byte[] data) => Hmac(d => Sha256(d), 64, key, data);

    /// <summary>RC4 stream cipher (symmetric).</summary>
    public static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256]; for (var i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; (s[i], s[j]) = (s[j], s[i]); }
        var o = new byte[data.Length]; int a = 0, b = 0;
        for (var k = 0; k < data.Length; k++)
        {
            a = (a + 1) & 255; b = (b + s[a]) & 255; (s[a], s[b]) = (s[b], s[a]);
            o[k] = (byte)(data[k] ^ s[(s[a] + s[b]) & 255]);
        }
        return o;
    }

    /// <summary>AES block cipher (128/192/256-bit keys).</summary>
    public sealed class Aes
    {
        readonly uint[] _enc, _dec;
        readonly int _rounds;

        /// <summary>Expands <paramref name="key"/> (16, 24 or 32 bytes).</summary>
        public Aes(ReadOnlySpan<byte> key)
        {
            if (key.Length is not (16 or 24 or 32)) throw new CryptographicException("AES key must be 16, 24 or 32 bytes.");
            var nk = key.Length / 4; _rounds = nk + 6;
            var total = 4 * (_rounds + 1);
            _enc = new uint[total];
            var sb = AesTables.Sbox;
            for (var i = 0; i < nk; i++) _enc[i] = BinaryPrimitives.ReadUInt32BigEndian(key[(i * 4)..]);
            uint rcon = 1;
            for (var i = nk; i < total; i++)
            {
                var t = _enc[i - 1];
                if (i % nk == 0) { t = SubWord(BitOperations.RotateLeft(t, 8), sb) ^ (rcon << 24); rcon = (rcon << 1) ^ ((rcon & 0x80) != 0 ? 0x11bu : 0); }
                else if (nk > 6 && i % nk == 4) t = SubWord(t, sb);
                _enc[i] = _enc[i - nk] ^ t;
            }
            // Equivalent inverse cipher: reverse the schedule and apply InvMixColumns to the middle round keys.
            _dec = new uint[total];
            var td = AesTables.Td0;
            for (var r = 0; r <= _rounds; r++)
                for (var c = 0; c < 4; c++)
                {
                    var w = _enc[(_rounds - r) * 4 + c];
                    if (r != 0 && r != _rounds)
                        w = td[sb[w >> 24]] ^ BitOperations.RotateRight(td[sb[(w >> 16) & 255]], 8) ^ BitOperations.RotateRight(td[sb[(w >> 8) & 255]], 16) ^ BitOperations.RotateRight(td[sb[w & 255]], 24);
                    _dec[r * 4 + c] = w;
                }
        }

        static uint SubWord(uint w, byte[] sb) => ((uint)sb[w >> 24] << 24) | ((uint)sb[(w >> 16) & 255] << 16) | ((uint)sb[(w >> 8) & 255] << 8) | sb[w & 255];

        /// <summary>Encrypts one 16-byte block.</summary>
        public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var te = AesTables.Te0; var sb = AesTables.Sbox; var rk = _enc;
            var s0 = BinaryPrimitives.ReadUInt32BigEndian(input) ^ rk[0];
            var s1 = BinaryPrimitives.ReadUInt32BigEndian(input[4..]) ^ rk[1];
            var s2 = BinaryPrimitives.ReadUInt32BigEndian(input[8..]) ^ rk[2];
            var s3 = BinaryPrimitives.ReadUInt32BigEndian(input[12..]) ^ rk[3];
            var p = 4;
            for (var r = 1; r < _rounds; r++, p += 4)
            {
                var t0 = te[s0 >> 24] ^ BitOperations.RotateRight(te[(s1 >> 16) & 255], 8) ^ BitOperations.RotateRight(te[(s2 >> 8) & 255], 16) ^ BitOperations.RotateRight(te[s3 & 255], 24) ^ rk[p];
                var t1 = te[s1 >> 24] ^ BitOperations.RotateRight(te[(s2 >> 16) & 255], 8) ^ BitOperations.RotateRight(te[(s3 >> 8) & 255], 16) ^ BitOperations.RotateRight(te[s0 & 255], 24) ^ rk[p + 1];
                var t2 = te[s2 >> 24] ^ BitOperations.RotateRight(te[(s3 >> 16) & 255], 8) ^ BitOperations.RotateRight(te[(s0 >> 8) & 255], 16) ^ BitOperations.RotateRight(te[s1 & 255], 24) ^ rk[p + 2];
                var t3 = te[s3 >> 24] ^ BitOperations.RotateRight(te[(s0 >> 16) & 255], 8) ^ BitOperations.RotateRight(te[(s1 >> 8) & 255], 16) ^ BitOperations.RotateRight(te[s2 & 255], 24) ^ rk[p + 3];
                s0 = t0; s1 = t1; s2 = t2; s3 = t3;
            }
            BinaryPrimitives.WriteUInt32BigEndian(output, Last(sb, s0, s1, s2, s3) ^ rk[p]);
            BinaryPrimitives.WriteUInt32BigEndian(output[4..], Last(sb, s1, s2, s3, s0) ^ rk[p + 1]);
            BinaryPrimitives.WriteUInt32BigEndian(output[8..], Last(sb, s2, s3, s0, s1) ^ rk[p + 2]);
            BinaryPrimitives.WriteUInt32BigEndian(output[12..], Last(sb, s3, s0, s1, s2) ^ rk[p + 3]);
        }

        /// <summary>Decrypts one 16-byte block.</summary>
        public void DecryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
        {
            var td = AesTables.Td0; var isb = AesTables.InvSbox; var rk = _dec;
            var s0 = BinaryPrimitives.ReadUInt32BigEndian(input) ^ rk[0];
            var s1 = BinaryPrimitives.ReadUInt32BigEndian(input[4..]) ^ rk[1];
            var s2 = BinaryPrimitives.ReadUInt32BigEndian(input[8..]) ^ rk[2];
            var s3 = BinaryPrimitives.ReadUInt32BigEndian(input[12..]) ^ rk[3];
            var p = 4;
            for (var r = 1; r < _rounds; r++, p += 4)
            {
                var t0 = td[s0 >> 24] ^ BitOperations.RotateRight(td[(s3 >> 16) & 255], 8) ^ BitOperations.RotateRight(td[(s2 >> 8) & 255], 16) ^ BitOperations.RotateRight(td[s1 & 255], 24) ^ rk[p];
                var t1 = td[s1 >> 24] ^ BitOperations.RotateRight(td[(s0 >> 16) & 255], 8) ^ BitOperations.RotateRight(td[(s3 >> 8) & 255], 16) ^ BitOperations.RotateRight(td[s2 & 255], 24) ^ rk[p + 1];
                var t2 = td[s2 >> 24] ^ BitOperations.RotateRight(td[(s1 >> 16) & 255], 8) ^ BitOperations.RotateRight(td[(s0 >> 8) & 255], 16) ^ BitOperations.RotateRight(td[s3 & 255], 24) ^ rk[p + 2];
                var t3 = td[s3 >> 24] ^ BitOperations.RotateRight(td[(s2 >> 16) & 255], 8) ^ BitOperations.RotateRight(td[(s1 >> 8) & 255], 16) ^ BitOperations.RotateRight(td[s0 & 255], 24) ^ rk[p + 3];
                s0 = t0; s1 = t1; s2 = t2; s3 = t3;
            }
            BinaryPrimitives.WriteUInt32BigEndian(output, Last(isb, s0, s3, s2, s1) ^ rk[p]);
            BinaryPrimitives.WriteUInt32BigEndian(output[4..], Last(isb, s1, s0, s3, s2) ^ rk[p + 1]);
            BinaryPrimitives.WriteUInt32BigEndian(output[8..], Last(isb, s2, s1, s0, s3) ^ rk[p + 2]);
            BinaryPrimitives.WriteUInt32BigEndian(output[12..], Last(isb, s3, s2, s1, s0) ^ rk[p + 3]);
        }

        static uint Last(byte[] box, uint a, uint b, uint c, uint d)
            => ((uint)box[a >> 24] << 24) | ((uint)box[(b >> 16) & 255] << 16) | ((uint)box[(c >> 8) & 255] << 8) | box[d & 255];

        /// <summary>ECB encryption of whole blocks (no padding).</summary>
        public byte[] EncryptEcb(ReadOnlySpan<byte> data)
        {
            RequireBlocks(data.Length);
            var o = new byte[data.Length];
            for (var i = 0; i < data.Length; i += 16) EncryptBlock(data.Slice(i, 16), o.AsSpan(i, 16));
            return o;
        }

        /// <summary>ECB decryption of whole blocks (no padding).</summary>
        public byte[] DecryptEcb(ReadOnlySpan<byte> data)
        {
            RequireBlocks(data.Length);
            var o = new byte[data.Length];
            for (var i = 0; i < data.Length; i += 16) DecryptBlock(data.Slice(i, 16), o.AsSpan(i, 16));
            return o;
        }

        /// <summary>CBC encryption of whole blocks (no padding).</summary>
        public byte[] EncryptCbcNoPad(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            RequireBlocks(data.Length); RequireIv(iv);
            var o = new byte[data.Length]; var prev = iv.ToArray(); var x = new byte[16];
            for (var i = 0; i < data.Length; i += 16)
            {
                for (var j = 0; j < 16; j++) x[j] = (byte)(data[i + j] ^ prev[j]);
                EncryptBlock(x, o.AsSpan(i, 16));
                o.AsSpan(i, 16).CopyTo(prev);
            }
            return o;
        }

        /// <summary>CBC decryption of whole blocks (no padding).</summary>
        public byte[] DecryptCbcNoPad(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            RequireBlocks(data.Length); RequireIv(iv);
            var o = new byte[data.Length]; var prev = iv.ToArray();
            for (var i = 0; i < data.Length; i += 16)
            {
                DecryptBlock(data.Slice(i, 16), o.AsSpan(i, 16));
                for (var j = 0; j < 16; j++) o[i + j] ^= prev[j];
                data.Slice(i, 16).CopyTo(prev);
            }
            return o;
        }

        /// <summary>CBC encryption with PKCS#7 padding.</summary>
        public byte[] EncryptCbcPkcs7(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            var pad = 16 - data.Length % 16;
            var padded = new byte[data.Length + pad];
            data.CopyTo(padded);
            padded.AsSpan(data.Length).Fill((byte)pad);
            return EncryptCbcNoPad(padded, iv);
        }

        /// <summary>CBC decryption that validates and strips PKCS#7 padding (throws <see cref="CryptographicException"/> when invalid).</summary>
        public byte[] DecryptCbcPkcs7(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            if (data.Length == 0) throw new CryptographicException("Padding is invalid and cannot be removed.");
            var plain = DecryptCbcNoPad(data, iv);
            var pad = plain[^1];
            // Constant-time padding validation: accumulate mismatches instead of branching on them.
            var bad = (pad == 0 || pad > 16) ? 1 : 0;
            for (var i = 0; i < 16; i++)
            {
                var inPad = i < pad ? 1 : 0;
                var differs = plain[plain.Length - 1 - i] != pad ? 1 : 0;
                bad |= inPad & differs;
            }
            if (bad != 0) throw new CryptographicException("Padding is invalid and cannot be removed.");
            return plain[..(plain.Length - pad)];
        }

        static void RequireBlocks(int length) { if (length % 16 != 0) throw new CryptographicException("AES input must be a multiple of 16 bytes."); }
        static void RequireIv(ReadOnlySpan<byte> iv) { if (iv.Length != 16) throw new CryptographicException("AES IV must be 16 bytes."); }
    }

    static class Md5Tables
    {
        internal static readonly uint[] K =
        [
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        ];
        internal static readonly int[] S =
        [
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        ];

    }

    // FIPS 180-4 constants: fractional parts of the square/cube roots of the first primes, computed exactly with integer roots.
    static class Sha2Tables
    {
        internal static readonly ulong[] K512, H512, H384;
        internal static readonly uint[] K256, H256;

        static Sha2Tables()
        {
            var primes = new List<int>();
            for (var n = 2; primes.Count < 80; n++)
                if (primes.All(p => n % p != 0)) primes.Add(n);
            K512 = new ulong[80];
            for (var i = 0; i < 80; i++) K512[i] = (ulong)(IntRoot((BigInteger)primes[i] << 192, 3) & ulong.MaxValue);
            H512 = new ulong[8]; H384 = new ulong[8];
            for (var i = 0; i < 8; i++)
            {
                H512[i] = (ulong)(IntRoot((BigInteger)primes[i] << 128, 2) & ulong.MaxValue);
                H384[i] = (ulong)(IntRoot((BigInteger)primes[i + 8] << 128, 2) & ulong.MaxValue);
            }
            K256 = new uint[64]; H256 = new uint[8];
            for (var i = 0; i < 64; i++) K256[i] = (uint)(K512[i] >> 32);
            for (var i = 0; i < 8; i++) H256[i] = (uint)(H512[i] >> 32);
        }

        static BigInteger IntRoot(BigInteger n, int degree)
        {
            var x = BigInteger.One << (int)(n.GetBitLength() / degree + 1);
            while (true)
            {
                var y = ((degree - 1) * x + n / BigInteger.Pow(x, degree - 1)) / degree;
                if (y >= x) return x;
                x = y;
            }
        }
    }

    static class AesTables
    {
        internal static readonly byte[] Sbox = new byte[256], InvSbox = new byte[256];
        internal static readonly uint[] Te0 = new uint[256], Td0 = new uint[256];

        static AesTables()
        {
            byte p = 1, q = 1;
            do
            {
                p = (byte)(p ^ (p << 1) ^ ((p & 0x80) != 0 ? 0x1B : 0));
                q ^= (byte)(q << 1); q ^= (byte)(q << 2); q ^= (byte)(q << 4);
                if ((q & 0x80) != 0) q ^= 0x09;
                var x = (byte)(q ^ Rol8(q, 1) ^ Rol8(q, 2) ^ Rol8(q, 3) ^ Rol8(q, 4));
                Sbox[p] = (byte)(x ^ 0x63);
            } while (p != 1);
            Sbox[0] = 0x63;
            for (var i = 0; i < 256; i++) InvSbox[Sbox[i]] = (byte)i;
            for (var i = 0; i < 256; i++)
            {
                uint s = Sbox[i], si = InvSbox[i];
                Te0[i] = (Mul(s, 2) << 24) | (s << 16) | (s << 8) | Mul(s, 3);
                Td0[i] = (Mul(si, 14) << 24) | (Mul(si, 9) << 16) | (Mul(si, 13) << 8) | Mul(si, 11);
            }
        }

        static byte Rol8(byte v, int n) => (byte)((v << n) | (v >> (8 - n)));

        static uint Mul(uint a, uint b)
        {
            uint r = 0;
            for (; b != 0; b >>= 1) { if ((b & 1) != 0) r ^= a; a <<= 1; if ((a & 0x100) != 0) a ^= 0x11b; }
            return r;
        }
    }
}
