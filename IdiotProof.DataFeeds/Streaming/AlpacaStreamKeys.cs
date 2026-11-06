namespace IdiotProof.DataFeeds.Streaming;

/// <summary>An Alpaca key pair for a websocket's auth message. Never printed.</summary>
public sealed record AlpacaStreamKeys(string KeyId, string Secret)
{
    public override string ToString() => "AlpacaStreamKeys(redacted)";
}
