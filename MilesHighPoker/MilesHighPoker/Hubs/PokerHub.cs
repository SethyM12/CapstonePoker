using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MilesHighPoker.Models;

namespace MilesHighPoker.Hubs;

public class PokerHub : Hub
{
    private static readonly ConcurrentDictionary<String, ConcurrentDictionary<String, WaitingPlayer>> WaitingByTable
        = new();

    public async Task SetDisplayName(String tableId, String name)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new HubException("Table id is required.");

        name = name?.Trim() ?? String.Empty;
        if (name.Length < 2 || name.Length > 20)
            throw new HubException("Name must be 2-20 characters.");

        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);

        ConcurrentDictionary<String, WaitingPlayer> table = WaitingByTable.GetOrAdd(
            tableId,
            _ => new ConcurrentDictionary<String, WaitingPlayer>()
        );

        table[Context.ConnectionId] = new WaitingPlayer(Context.ConnectionId, name, DateTime.UtcNow);

        await BroadcastWaitingPlayers(tableId);
    }

    public Task<List<WaitingPlayer>> GetWaitingPlayers(String tableId)
    {
        if (!WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? table))
            return Task.FromResult(new List<WaitingPlayer>());

        List<WaitingPlayer> players = table.Values
            .OrderBy(p => p.Joined)
            .ToList();

        return Task.FromResult(players);
    }

    public async Task SendAction(String tableId, String action)
    {
        await Clients.Group(tableId).SendAsync("PlayerActionReceived", Context.ConnectionId, action);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach ((String tableId, ConcurrentDictionary<String, WaitingPlayer> table) in WaitingByTable)
        {
            if (table.TryRemove(Context.ConnectionId, out _))
                await BroadcastWaitingPlayers(tableId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastWaitingPlayers(String tableId)
    {
        List<WaitingPlayer> players = await GetWaitingPlayers(tableId);
        await Clients.Group(tableId).SendAsync("WaitingPlayersUpdated", players);
    }
    
    public async Task JoinTable(String tableId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new HubException("Table id is required.");

        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
    }
}
