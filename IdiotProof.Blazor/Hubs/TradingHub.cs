using Microsoft.AspNetCore.SignalR;

namespace IdiotProof.Blazor.Hubs;

/// <summary>
/// SignalR hub for real-time trading updates.
/// Clients join symbol groups to receive live price and signal updates.
/// </summary>
public sealed class TradingHub : Hub
{
    /// <summary>Subscribe the current connection to updates for a specific ticker symbol.</summary>
    public async Task JoinTicker(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }

    /// <summary>Unsubscribe the current connection from updates for a specific ticker symbol.</summary>
    public async Task LeaveTicker(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol.ToUpperInvariant());
    }

    /// <summary>Join the broadcast group to receive all signals regardless of symbol.</summary>
    public async Task JoinBroadcast()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "broadcast");
    }

    /// <summary>Leave the broadcast group.</summary>
    public async Task LeaveBroadcast()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "broadcast");
    }
}
