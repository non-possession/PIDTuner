using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

internal sealed record S7DbReadBlock(
    int DataBlock,
    int StartByte,
    int ByteCount,
    IReadOnlyList<S7Address> Addresses)
{
    public int EndByteExclusive => StartByte + ByteCount;
}

internal static class S7DbReadPlanner
{
    // A second ReadVar exchange adds request, response and item headers. Reading a smaller DB
    // hole costs fewer wire bytes than starting another request, so it remains in the same block.
    internal const int EstimatedAdditionalRoundTripBytes = 42;

    public static IReadOnlyList<S7DbReadBlock> Plan(
        IReadOnlyList<S7Address> addresses,
        int maximumPayloadBytes)
    {
        if (maximumPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPayloadBytes),
                maximumPayloadBytes,
                "S7 maximum payload must be greater than zero.");
        }

        var blocks = new List<S7DbReadBlock>();
        foreach (var dbGroup in addresses.GroupBy(address => address.DataBlock).OrderBy(group => group.Key))
        {
            var ordered = dbGroup
                .OrderBy(address => address.ByteOffset)
                .ThenBy(address => address.BitOffset ?? 0)
                .ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var current = new List<S7Address> { ordered[0] };
            var currentStart = ordered[0].ByteOffset;
            var currentEnd = ordered[0].ByteOffset + ordered[0].ReadByteCount;
            EnsureAddressFits(ordered[0], maximumPayloadBytes);

            foreach (var address in ordered.Skip(1))
            {
                EnsureAddressFits(address, maximumPayloadBytes);
                var addressEnd = address.ByteOffset + address.ReadByteCount;
                var candidateEnd = Math.Max(currentEnd, addressEnd);
                var candidateLength = candidateEnd - currentStart;
                var gap = Math.Max(0, address.ByteOffset - currentEnd);
                var shouldSplit = candidateLength > maximumPayloadBytes
                    || gap > EstimatedAdditionalRoundTripBytes;

                if (shouldSplit)
                {
                    blocks.Add(CreateBlock(dbGroup.Key, currentStart, currentEnd, current));
                    current = new List<S7Address>();
                    currentStart = address.ByteOffset;
                    currentEnd = addressEnd;
                }
                else
                {
                    currentEnd = candidateEnd;
                }

                current.Add(address);
            }

            blocks.Add(CreateBlock(dbGroup.Key, currentStart, currentEnd, current));
        }

        return blocks;
    }

    public static int CalculateMaximumReadPayload(int negotiatedPduLength)
    {
        const int s7AckHeaderBytes = 12;
        const int readResponseParameterBytes = 2;
        const int readItemHeaderBytes = 4;
        return Math.Max(1, negotiatedPduLength - s7AckHeaderBytes - readResponseParameterBytes - readItemHeaderBytes);
    }

    private static S7DbReadBlock CreateBlock(
        int dataBlock,
        int startByte,
        int endByteExclusive,
        IReadOnlyList<S7Address> addresses) =>
        new(dataBlock, startByte, endByteExclusive - startByte, addresses.ToArray());

    private static void EnsureAddressFits(S7Address address, int maximumPayloadBytes)
    {
        if (address.ReadByteCount > maximumPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Address DB{address.DataBlock}.DBB{address.ByteOffset} requires {address.ReadByteCount} bytes, " +
                $"which exceeds the negotiated S7 payload of {maximumPayloadBytes} bytes.");
        }
    }
}
