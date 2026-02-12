namespace EvidenceFoundry.Helpers;

public static class DeterministicSeedHelper
{
    public static int CreateSeed(string scope, params string?[] parts)
    {
        var guid = DeterministicIdHelper.CreateGuid(scope, parts);
        var bytes = guid.ToByteArray();
        var seed = BitConverter.ToInt32(bytes, 0);
        return seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
    }
}
