using System.Security.Cryptography;

namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 targets one game build; its patch targets and field names are meaningless on another.
/// Same check the other MODs in this repository make (SPEC004 10.3).
/// </summary>
internal static class BuildFingerprint
{
    internal const string ExpectedGameAssemblySha256 =
        "B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D";

    internal const string ExpectedMetadataSha256 =
        "A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B";

    internal static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
