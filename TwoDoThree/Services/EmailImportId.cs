using System.IO;
using System.Security.Cryptography;

namespace TwoDoThree.Services;

public static class EmailImportId
{
    public static string CreateForFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
