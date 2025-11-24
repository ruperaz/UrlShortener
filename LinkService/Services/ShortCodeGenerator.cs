using System.Security.Cryptography;
using System.Text;

namespace LinkService.Services;

public static class ShortCodeGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static string Generate(int length = 8)
    {
        var bytes = new byte[length];
        Rng.GetBytes(bytes);
        var chars = new char[length];

        for (int i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}