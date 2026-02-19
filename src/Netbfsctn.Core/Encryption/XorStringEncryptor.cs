using System.Security.Cryptography;
using System.Text;

namespace Netbfsctn.Core.Encryption;

public class XorStringEncryptor : IStringEncryptor
{
    public byte[] Encrypt(string plainText, byte[] key)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var result = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            result[i] = (byte)(bytes[i] ^ key[i % key.Length]);
        }
        return result;
    }

    public string Decrypt(byte[] encrypted, byte[] key)
    {
        var result = new byte[encrypted.Length];
        for (var i = 0; i < encrypted.Length; i++)
        {
            result[i] = (byte)(encrypted[i] ^ key[i % key.Length]);
        }
        return Encoding.UTF8.GetString(result);
    }

    public byte[] GenerateKey(int length = 16)
    {
        return RandomNumberGenerator.GetBytes(length);
    }
}
