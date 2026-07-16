using System.Security.Cryptography;

namespace BattleGame.Online;

public sealed class RandomRoomCodeGenerator : IRoomCodeGenerator
{
    // 排除 0/O、1/I 等易混淆字符，降低语音或聊天传递房间码时的输入错误。
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;

    public string Create()
    {
        Span<char> code = stackalloc char[CodeLength];
        for (int index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}

internal static class SecureTokenGenerator
{
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool Equals(string expected, string actual)
    {
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        byte[] actualBytes = System.Text.Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
