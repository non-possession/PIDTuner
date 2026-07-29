using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Plc;

public sealed record S7Address(
    int DataBlock,
    int ByteOffset,
    int? BitOffset,
    PlcDataType DataType)
{
    public int ReadByteCount => DataType switch
    {
        PlcDataType.Boolean => 1,
        PlcDataType.Int16 => 2,
        PlcDataType.Int32 => 4,
        PlcDataType.Float => 4,
        PlcDataType.Double => 4,
        _ => throw new NotSupportedException($"Unsupported PLC data type: {DataType}.")
    };

    public int BitAddress => ByteOffset * 8 + (BitOffset ?? 0);
}
