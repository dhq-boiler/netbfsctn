namespace Netbfsctn.Core.Encryption;

public interface IStringEncryptor
{
    byte[] Encrypt(string plainText, byte[] key);
    string Decrypt(byte[] encrypted, byte[] key);
    byte[] GenerateKey(int length = 16);
}
