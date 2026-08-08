using System.Security.Cryptography;

namespace SiNiSistar2.Edi.Core;

public static class BuildFingerprint
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    public static bool Matches(TargetGameBuild expected, string gameAssemblyPath, string metadataPath) =>
        string.Equals(ComputeSha256(gameAssemblyPath), expected.GameAssemblySha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ComputeSha256(metadataPath), expected.GlobalMetadataSha256, StringComparison.OrdinalIgnoreCase);
}
