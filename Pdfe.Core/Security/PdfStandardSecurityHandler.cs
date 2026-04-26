using System.Security.Cryptography;
using Pdfe.Core.Parsing;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Security;

/// <summary>
/// PDF Standard Security Handler — derives the file encryption key
/// from a user password, verifies it against the /U and /O entries,
/// and produces per-object keys for stream/string decryption.
///
/// References: ISO 32000-2:2020 §7.6.3 (Standard Security Handler),
/// Algorithms 2 (key derivation), 4-5 (encryption), 6 (password
/// verification). Algorithm numbering matches the spec.
///
/// This implementation supports:
/// - V=1 R=2 (40-bit RC4) — legacy
/// - V=2 R=3 (128-bit RC4) — most common
/// - V=4 R=4 with CFM=V2 (RC4-128 with crypt filters) — common
///
/// AES (V=4 R=4 CFM=AESV2 and V=5 R=6) is NOT yet implemented;
/// callers will get a NotSupportedException for those files until
/// the AES path lands.
/// </summary>
public sealed class PdfStandardSecurityHandler
{
    /// <summary>32-byte padding string used by Algorithms 2 and 6.</summary>
    private static readonly byte[] PasswordPadding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    };

    /// <summary>Encryption algorithm version (/V).</summary>
    public int V { get; }

    /// <summary>Standard security handler revision (/R).</summary>
    public int R { get; }

    /// <summary>Encryption key length in bytes (e.g. 5 for V=1, 16 for V=2/4).</summary>
    public int KeyLengthBytes { get; }

    /// <summary>The derived file encryption key for stream/string keys.</summary>
    private readonly byte[] _fileEncryptionKey;

    /// <summary>True if streams/strings use AES (V=4 with CFM=AESV2 or V=5).</summary>
    public bool UsesAes { get; }

    private PdfStandardSecurityHandler(int v, int r, int keyLengthBytes, byte[] fileKey, bool usesAes)
    {
        V = v;
        R = r;
        KeyLengthBytes = keyLengthBytes;
        _fileEncryptionKey = fileKey;
        UsesAes = usesAes;
    }

    /// <summary>
    /// Build a handler from the /Encrypt dict and document /ID, verifying
    /// the supplied user password. Throws <see cref="PdfEncryptionNotSupportedException"/>
    /// for unsupported algorithm versions and a clear message when the
    /// password is wrong.
    /// </summary>
    /// <param name="encryptDict">The /Encrypt dictionary from the trailer.</param>
    /// <param name="firstId">First element of the trailer /ID array.</param>
    /// <param name="userPassword">User password bytes (empty array for empty password — the most common case).</param>
    public static PdfStandardSecurityHandler Build(
        PdfDictionary encryptDict, byte[] firstId, byte[] userPassword)
    {
        var filter = encryptDict.GetNameOrNull("Filter");
        if (filter != "Standard")
            throw new PdfEncryptionNotSupportedException(
                $"Only the Standard security handler is supported (file uses /Filter /{filter}).");

        int v = encryptDict.GetInt("V");
        int r = encryptDict.GetInt("R");
        int lengthBits = encryptDict.ContainsKey("Length") ? encryptDict.GetInt("Length") : 40;
        // /Length is in bits per spec; some files (incorrectly) store bytes.
        // Bytes vs bits is unambiguous: encryption keys are 40-128 bits = 5-16 bytes.
        int keyBytes = lengthBits >= 40 ? lengthBits / 8 : lengthBits;
        if (keyBytes < 5 || keyBytes > 32)
            throw new PdfEncryptionNotSupportedException(
                $"Unsupported /Length value {lengthBits} in /Encrypt dict.");

        // Determine cipher: RC4 (V=1, V=2) or look at /CF for V=4
        bool usesAes = false;
        if (v == 4 || v == 5)
        {
            var cf = encryptDict.GetDictionaryOrNull("CF");
            var stmF = encryptDict.GetNameOrNull("StmF") ?? "Identity";
            if (cf != null && stmF != "Identity")
            {
                var cfm = cf.GetDictionaryOrNull(stmF)?.GetNameOrNull("CFM");
                if (cfm == "AESV2" || cfm == "AESV3")
                    usesAes = true;
            }
        }

        if (v == 5 || (v == 4 && usesAes))
        {
            throw new PdfEncryptionNotSupportedException(
                $"AES decryption (V={v}, CFM={(usesAes ? "AESV2/V3" : "?")}) is not yet implemented. " +
                "Tracked in GitHub #324; RC4 is currently supported.");
        }
        if (v != 1 && v != 2 && v != 4)
            throw new PdfEncryptionNotSupportedException(
                $"Encryption algorithm V={v} is not supported. Only V=1, V=2, V=4 (RC4) are implemented.");

        // /O, /U: usually 32-byte hex strings. Read as raw bytes.
        var oBytes = GetByteString(encryptDict, "O", 32);
        var uBytes = GetByteString(encryptDict, "U", 32);
        // /P is a 32-bit signed integer of permission flags.
        long permissions = encryptDict.GetInt("P");
        bool encryptMetadata = !encryptDict.ContainsKey("EncryptMetadata") ||
                               encryptDict.GetBool("EncryptMetadata");

        // Algorithm 2: derive file encryption key.
        var fileKey = DeriveFileKey(
            userPassword, oBytes, permissions, firstId, r, keyBytes, encryptMetadata);

        // Algorithm 6: verify the user password.
        if (!VerifyUserPassword(fileKey, uBytes, firstId, r))
        {
            throw new PdfEncryptionNotSupportedException(
                "Password verification failed. The file requires a non-empty user password, " +
                "which pdfe doesn't yet prompt for. Pass allowEncrypted: true to inspect the " +
                "encryption dict, or open the file in a tool that supports password input.");
        }

        return new PdfStandardSecurityHandler(v, r, keyBytes, fileKey, usesAes);
    }

    /// <summary>
    /// Decrypt a stream's encoded bytes for the given indirect object. The
    /// returned bytes are still subject to the stream's /Filter pipeline
    /// (FlateDecode etc.) — Crypt is conceptually the first filter.
    /// </summary>
    public byte[] DecryptStream(int objNum, int gen, byte[] cipherBytes)
        => Rc4.Transform(DeriveObjectKey(objNum, gen), cipherBytes);

    /// <summary>
    /// Decrypt a PDF string belonging to the given indirect object.
    /// </summary>
    public byte[] DecryptString(int objNum, int gen, byte[] cipherBytes)
        => Rc4.Transform(DeriveObjectKey(objNum, gen), cipherBytes);

    /// <summary>
    /// Algorithm 1: per-object key from file key + obj# + gen#.
    /// Used by both string and stream decryption with RC4.
    /// </summary>
    private byte[] DeriveObjectKey(int objNum, int gen)
    {
        // Append low-order 3 bytes of obj#, low-order 2 bytes of gen# (LE).
        var input = new byte[_fileEncryptionKey.Length + 5];
        Array.Copy(_fileEncryptionKey, input, _fileEncryptionKey.Length);
        int p = _fileEncryptionKey.Length;
        input[p + 0] = (byte)(objNum & 0xFF);
        input[p + 1] = (byte)((objNum >> 8) & 0xFF);
        input[p + 2] = (byte)((objNum >> 16) & 0xFF);
        input[p + 3] = (byte)(gen & 0xFF);
        input[p + 4] = (byte)((gen >> 8) & 0xFF);

        var hash = MD5.HashData(input);
        // Object key length: min(filekey + 5, 16)
        int n = Math.Min(_fileEncryptionKey.Length + 5, 16);
        var key = new byte[n];
        Array.Copy(hash, key, n);
        return key;
    }

    /// <summary>
    /// Algorithm 2: file encryption key from password + /O + /P + /ID[0].
    /// </summary>
    private static byte[] DeriveFileKey(
        byte[] password, byte[] o, long p, byte[] firstId,
        int r, int keyBytes, bool encryptMetadata)
    {
        // Step 1-2: pad/truncate password to 32 bytes
        var paddedPwd = PadPassword(password);

        // Step 3-7: MD5 of padded password + O + P + ID[0]
        using var md5 = MD5.Create();
        md5.TransformBlock(paddedPwd, 0, 32, null, 0);
        md5.TransformBlock(o, 0, 32, null, 0);
        var pBytes = new byte[]
        {
            (byte)(p & 0xFF),
            (byte)((p >> 8) & 0xFF),
            (byte)((p >> 16) & 0xFF),
            (byte)((p >> 24) & 0xFF)
        };
        md5.TransformBlock(pBytes, 0, 4, null, 0);
        md5.TransformBlock(firstId, 0, firstId.Length, null, 0);

        // Step 6 (R≥4): if /EncryptMetadata is false, hash 4 bytes of 0xFF.
        if (r >= 4 && !encryptMetadata)
        {
            var ff = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            md5.TransformBlock(ff, 0, 4, null, 0);
        }

        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = md5.Hash!;

        // Step 8 (R≥3): re-hash 50 times, taking only first keyBytes bytes
        if (r >= 3)
        {
            for (int i = 0; i < 50; i++)
            {
                var input = new byte[keyBytes];
                Array.Copy(hash, input, keyBytes);
                hash = MD5.HashData(input);
            }
        }

        // Step 9: take first keyBytes
        var key = new byte[keyBytes];
        Array.Copy(hash, key, keyBytes);
        return key;
    }

    /// <summary>
    /// Algorithm 6: verify user password by comparing against /U.
    /// R=2: encrypt the padding string with the file key; compare to /U.
    /// R≥3: MD5(padding || ID[0]) → 20 RC4 rounds with key XOR i; compare first 16 bytes.
    /// </summary>
    private static bool VerifyUserPassword(byte[] fileKey, byte[] u, byte[] firstId, int r)
    {
        if (r == 2)
        {
            var encrypted = Rc4.Transform(fileKey, PasswordPadding);
            return ByteArrayEquals(encrypted, u);
        }

        // R ≥ 3
        using var md5 = MD5.Create();
        md5.TransformBlock(PasswordPadding, 0, 32, null, 0);
        md5.TransformBlock(firstId, 0, firstId.Length, null, 0);
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = md5.Hash!;

        var encrypted2 = Rc4.Transform(fileKey, hash);
        for (int i = 1; i <= 19; i++)
        {
            var keyMod = new byte[fileKey.Length];
            for (int j = 0; j < fileKey.Length; j++)
                keyMod[j] = (byte)(fileKey[j] ^ i);
            encrypted2 = Rc4.Transform(keyMod, encrypted2);
        }

        // Compare first 16 bytes only — /U for R≥3 is 32 bytes, but the
        // last 16 are arbitrary salt.
        for (int i = 0; i < 16; i++)
            if (encrypted2[i] != u[i]) return false;
        return true;
    }

    private static byte[] PadPassword(byte[] password)
    {
        var padded = new byte[32];
        int copyLen = Math.Min(password.Length, 32);
        if (copyLen > 0) Array.Copy(password, padded, copyLen);
        if (copyLen < 32) Array.Copy(PasswordPadding, 0, padded, copyLen, 32 - copyLen);
        return padded;
    }

    private static byte[] GetByteString(PdfDictionary dict, string name, int expectedLength)
    {
        var s = dict.GetOptional(name) as PdfString
            ?? throw new PdfParseException($"/Encrypt dict is missing /{name}");
        var bytes = s.Bytes;
        if (bytes.Length != expectedLength)
            throw new PdfParseException(
                $"/{name} must be exactly {expectedLength} bytes; got {bytes.Length}");
        return bytes;
    }

    private static bool ByteArrayEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
