using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

public static class ScryptPassword
{
    private const int N = 16384;
    private const int R = 8;
    private const int P = 1;
    private const int KeyLength = 64;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var derived = Scrypt(Encoding.UTF8.GetBytes(password), salt, KeyLength);
        return $"{Convert.ToHexString(salt).ToLowerInvariant()}:{Convert.ToHexString(derived).ToLowerInvariant()}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromHexString(parts[0]);
            var expected = Convert.FromHexString(parts[1]);
            var actual = Scrypt(Encoding.UTF8.GetBytes(password), salt, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Scrypt(byte[] password, byte[] salt, int derivedLength)
    {
        var blockSize = 128 * R;
        var b = Pbkdf2(password, salt, P * blockSize);
        var xy = new byte[256 * R];
        var v = new byte[N * blockSize];

        for (var i = 0; i < P; i++)
        {
            var offset = i * blockSize;
            Smix(b.AsSpan(offset, blockSize), R, N, v, xy);
        }

        return Pbkdf2(password, b, derivedLength);
    }

    private static byte[] Pbkdf2(byte[] password, byte[] salt, int length)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, length);
    }

    private static void Smix(Span<byte> b, int r, int n, byte[] v, byte[] xy)
    {
        var blockSize = 128 * r;
        var x = xy.AsSpan(0, blockSize);
        var y = xy.AsSpan(blockSize, blockSize);
        b.CopyTo(x);

        for (var i = 0; i < n; i++)
        {
            x.CopyTo(v.AsSpan(i * blockSize, blockSize));
            BlockMix(x, y, r);
            y.CopyTo(x);
        }

        for (var i = 0; i < n; i++)
        {
            var j = (int)(Integerify(x, r) & (ulong)(n - 1));
            Xor(x, v.AsSpan(j * blockSize, blockSize));
            BlockMix(x, y, r);
            y.CopyTo(x);
        }

        x.CopyTo(b);
    }

    private static ulong Integerify(ReadOnlySpan<byte> b, int r)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(b.Slice((2 * r - 1) * 64, 8));
    }

    private static void BlockMix(ReadOnlySpan<byte> b, Span<byte> y, int r)
    {
        Span<byte> x = stackalloc byte[64];
        b.Slice((2 * r - 1) * 64, 64).CopyTo(x);

        for (var i = 0; i < 2 * r; i++)
        {
            Xor(x, b.Slice(i * 64, 64));
            Salsa208(x);
            var destination = i % 2 == 0 ? (i / 2) * 64 : (r + i / 2) * 64;
            x.CopyTo(y.Slice(destination, 64));
        }
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= source[i];
        }
    }

    private static void Salsa208(Span<byte> block)
    {
        Span<uint> x = stackalloc uint[16];
        Span<uint> original = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
        {
            x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(i * 4, 4));
            original[i] = x[i];
        }

        for (var i = 0; i < 8; i += 2)
        {
            QuarterRound(x, 0, 4, 8, 12);
            QuarterRound(x, 5, 9, 13, 1);
            QuarterRound(x, 10, 14, 2, 6);
            QuarterRound(x, 15, 3, 7, 11);
            QuarterRound(x, 0, 1, 2, 3);
            QuarterRound(x, 5, 6, 7, 4);
            QuarterRound(x, 10, 11, 8, 9);
            QuarterRound(x, 15, 12, 13, 14);
        }

        for (var i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(i * 4, 4), x[i] + original[i]);
        }
    }

    private static void QuarterRound(Span<uint> x, int a, int b, int c, int d)
    {
        x[b] ^= RotateLeft(x[a] + x[d], 7);
        x[c] ^= RotateLeft(x[b] + x[a], 9);
        x[d] ^= RotateLeft(x[c] + x[b], 13);
        x[a] ^= RotateLeft(x[d] + x[c], 18);
    }

    private static uint RotateLeft(uint value, int offset)
    {
        return (value << offset) | (value >> (32 - offset));
    }
}
