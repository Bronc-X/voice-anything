namespace SayAll.HidBridge.Contracts;

public static class FridaGadgetContract
{
    public const string Version = "17.15.3";
    public const long DllLength = 23_575_552;
    public const string DllSha256 =
        "6FCA4007B2284C765A6C15C967A741F536B5865BF83867326A54029A3B752748";

    public static bool IsSupported(long length, string sha256)
    {
        return length == DllLength &&
            string.Equals(sha256, DllSha256, StringComparison.OrdinalIgnoreCase);
    }
}
