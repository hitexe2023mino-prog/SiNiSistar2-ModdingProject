namespace SiNiSistar2.Spawn.Plugin;

/// <summary>
/// SPEC004 targets one game build; its patch targets and field names are meaningless on another.
/// Same check the other MODs in this repository make (SPEC004 10.3).
///
/// The hashing itself moved to the shared <c>StartupGuard</c> so the 51 MB read happens once per
/// process rather than once per MOD (REFACTOR001 RF-001). What stays here is what is specific to
/// this MOD: the build it was actually measured against.
/// </summary>
internal static class BuildFingerprint
{
    internal const string ExpectedGameAssemblySha256 =
        "B869493305BBE587598C8709E7FE271F00D79D37803C6A8241946D6A6297499D";

    internal const string ExpectedMetadataSha256 =
        "A56278D0162B6C148312B56FBE208B54BA9AF2D3BAD609EBF9349B7AE7DDC84B";
}
