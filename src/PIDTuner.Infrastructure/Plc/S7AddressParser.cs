using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Plc;

public static class S7AddressParser
{
    public static S7Address Parse(string address, PlcDataType dataType)
    {
        var text = address.Trim().ToUpperInvariant();
        if (!text.StartsWith("DB", StringComparison.Ordinal))
        {
            throw new FormatException($"Only DB addresses are supported in the current S7 reader: {address}");
        }

        var dotIndex = text.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex <= 2)
        {
            throw new FormatException($"Invalid S7 DB address: {address}");
        }

        var dbNumberText = text[2..dotIndex];
        if (!int.TryParse(dbNumberText, out var dbNumber) || dbNumber <= 0)
        {
            throw new FormatException($"Invalid S7 DB number: {address}");
        }

        var offsetText = text[(dotIndex + 1)..];
        if (offsetText.StartsWith("DBX", StringComparison.Ordinal))
        {
            var bitSeparator = offsetText.IndexOf('.', StringComparison.Ordinal);
            if (bitSeparator <= 3)
            {
                throw new FormatException($"Invalid S7 bit address: {address}");
            }

            var byteText = offsetText[3..bitSeparator];
            var bitText = offsetText[(bitSeparator + 1)..];
            if (!int.TryParse(byteText, out var byteOffset)
                || !int.TryParse(bitText, out var bitOffset)
                || byteOffset < 0
                || bitOffset is < 0 or > 7)
            {
                throw new FormatException($"Invalid S7 bit address: {address}");
            }

            return new S7Address(dbNumber, byteOffset, bitOffset, PlcDataType.Boolean);
        }

        var prefix = GetOffsetPrefix(offsetText);
        var numericText = offsetText[prefix.Length..];
        if (!int.TryParse(numericText, out var offset) || offset < 0)
        {
            throw new FormatException($"Invalid S7 byte offset: {address}");
        }

        return new S7Address(dbNumber, offset, null, dataType);
    }

    private static string GetOffsetPrefix(string offsetText)
    {
        foreach (var prefix in new[] { "DBD", "DBW", "DBB" })
        {
            if (offsetText.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        throw new FormatException($"Unsupported S7 DB offset syntax: {offsetText}");
    }
}
