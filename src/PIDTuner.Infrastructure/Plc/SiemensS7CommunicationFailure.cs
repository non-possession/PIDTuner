using System.Net.Sockets;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

internal sealed record SiemensS7CommunicationFailure(
    PlcCommunicationErrorCategory Category,
    string Code,
    string Context,
    bool IsTransient,
    string Message)
{
    public static SiemensS7CommunicationFailure FromException(Exception exception)
    {
        if (exception is SiemensS7ProtocolException protocolException)
        {
            return new SiemensS7CommunicationFailure(
                protocolException.Category,
                protocolException.Code,
                protocolException.Context,
                protocolException.IsTransient,
                protocolException.Message);
        }

        if (exception is TimeoutException)
        {
            return new SiemensS7CommunicationFailure(
                PlcCommunicationErrorCategory.Timeout,
                "S7.TIMEOUT",
                "S7 request",
                true,
                exception.Message);
        }

        if (exception is IOException or SocketException)
        {
            return new SiemensS7CommunicationFailure(
                PlcCommunicationErrorCategory.Connection,
                "S7.CONNECTION_IO",
                "TCP stream",
                true,
                exception.Message);
        }

        if (exception is FormatException or NotSupportedException)
        {
            return new SiemensS7CommunicationFailure(
                PlcCommunicationErrorCategory.Parsing,
                "S7.VALUE_PARSE",
                "S7 value decoding",
                false,
                exception.Message);
        }

        return new SiemensS7CommunicationFailure(
            PlcCommunicationErrorCategory.Unknown,
            "S7.UNKNOWN",
            exception.GetType().Name,
            false,
            exception.Message);
    }
}

internal sealed class SiemensS7ProtocolException(
    PlcCommunicationErrorCategory category,
    string code,
    string context,
    string message,
    bool isTransient = false,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public PlcCommunicationErrorCategory Category { get; } = category;

    public string Code { get; } = code;

    public string Context { get; } = context;

    public bool IsTransient { get; } = isTransient;
}
