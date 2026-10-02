using Stanza.Core;

namespace Stanza.Protocol.Xeps.Sharing;

public enum FileTransferDirection
{
    Outgoing,
    Incoming
}

public enum FileTransferState
{
    Pending,
    Accepted,
    Transferring,
    Completed,
    Rejected,
    Cancelled,
    Failed
}

public sealed record JingleFileTransferOffer(
    string SessionId,
    string Name,
    long Size,
    string MediaType,
    Jid To,
    DateTimeOffset CreatedAtUtc);

public sealed record JingleFileTransferSessionEventArgs(
    string SessionId,
    Jid Peer,
    string? Name,
    long? Size,
    string? MediaType);

public sealed record JingleFileTransferProgressEventArgs(
    string SessionId,
    Jid Peer,
    long BytesTransferred,
    long TotalBytes,
    double Percent);

public sealed class JingleFileTransferSession
{
    public string SessionId { get; init; } = string.Empty;
    public Jid Peer { get; init; } = null!;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string MediaType { get; init; } = "application/octet-stream";
    public FileTransferDirection Direction { get; init; }
    public FileTransferState State { get; set; } = FileTransferState.Pending;
    public int BlockSize { get; set; } = 4096;
    public long BytesTransferred { get; set; }
    public double Progress => FileSize > 0 ? Math.Clamp((double)BytesTransferred / FileSize, 0.0, 1.0) : (State == FileTransferState.Completed ? 1.0 : 0.0);
    public string? ErrorMessage { get; set; }
    public byte[]? Data { get; set; }
    public MemoryStream? ReceiveBuffer { get; set; }
    public string? LocalSavePath { get; set; }

    internal TaskCompletionSource<bool> AcceptedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<byte[]> CompletedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationTokenSource Cts { get; } = new();
}
