namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static bool IsValidAgeX25519Recipient(string? value)
    {
        const string alphabet = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        if (value is null || value.Length != 62 || !value.StartsWith("age1", StringComparison.Ordinal))
            return false;

        static uint Step(uint current, int digit)
        {
            var high = current >> 25;
            current = ((current & 0x1ffffff) << 5) ^ (uint)digit;
            if ((high & 1) != 0) current ^= 0x3b6a57b2;
            if ((high & 2) != 0) current ^= 0x26508e6d;
            if ((high & 4) != 0) current ^= 0x1ea119fa;
            if ((high & 8) != 0) current ^= 0x3d4233dd;
            if ((high & 16) != 0) current ^= 0x2a1462b3;
            return current;
        }

        uint checksum = 1;
        foreach (var character in "age") checksum = Step(checksum, character >> 5);
        checksum = Step(checksum, 0);
        foreach (var character in "age") checksum = Step(checksum, character & 31);
        for (var index = 4; index < value.Length; index++)
        {
            var digit = alphabet.IndexOf(value[index]);
            if (digit < 0)
                return false;
            checksum = Step(checksum, digit);
        }

        // The 52nd data digit holds one remaining key bit and four zero padding bits.
        return checksum == 1 && (alphabet.IndexOf(value[55]) & 0x0f) == 0;
    }
}
