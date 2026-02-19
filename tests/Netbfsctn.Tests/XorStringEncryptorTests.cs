using Netbfsctn.Core.Encryption;

namespace Netbfsctn.Tests;

public class XorStringEncryptorTests
{
    [Fact]
    public void EncryptDecrypt_Roundtrip()
    {
        var encryptor = new XorStringEncryptor();
        var key = encryptor.GenerateKey();
        var original = "Hello, World!";

        var encrypted = encryptor.Encrypt(original, key);
        var decrypted = encryptor.Decrypt(encrypted, key);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentBytes()
    {
        var encryptor = new XorStringEncryptor();
        var key = encryptor.GenerateKey();
        var original = "Test String";

        var encrypted = encryptor.Encrypt(original, key);
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(original);

        Assert.NotEqual(originalBytes, encrypted);
    }

    [Fact]
    public void EncryptDecrypt_EmptyString()
    {
        var encryptor = new XorStringEncryptor();
        var key = encryptor.GenerateKey();

        var encrypted = encryptor.Encrypt("", key);
        var decrypted = encryptor.Decrypt(encrypted, key);

        Assert.Equal("", decrypted);
    }

    [Fact]
    public void EncryptDecrypt_JapaneseText()
    {
        var encryptor = new XorStringEncryptor();
        var key = encryptor.GenerateKey();
        var original = "こんにちは世界";

        var encrypted = encryptor.Encrypt(original, key);
        var decrypted = encryptor.Decrypt(encrypted, key);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void GenerateKey_ProducesCorrectLength()
    {
        var encryptor = new XorStringEncryptor();

        var key16 = encryptor.GenerateKey(16);
        Assert.Equal(16, key16.Length);

        var key32 = encryptor.GenerateKey(32);
        Assert.Equal(32, key32.Length);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentEncryption()
    {
        var encryptor = new XorStringEncryptor();
        var key1 = encryptor.GenerateKey();
        var key2 = encryptor.GenerateKey();
        var original = "Same input";

        var encrypted1 = encryptor.Encrypt(original, key1);
        var encrypted2 = encryptor.Encrypt(original, key2);

        Assert.NotEqual(encrypted1, encrypted2);
    }
}
