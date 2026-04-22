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
    
        name = (name ?? String.Empty).Trim();
        if (name.Length < 2 || name.Length > 20)
            throw new HubException("Name must be 2-20 characters.");
    
        ConcurrentDictionary<String, WaitingPlayer> table = WaitingByTable.GetOrAdd(
            tableId,
            _ => new ConcurrentDictionary<String, WaitingPlayer>()
        );
    
        DateTime joinedAt = DateTime.UtcNow;
    
        // Atomic check + write so duplicate names cannot slip in under concurrency.
        lock (table)
        {
            Boolean nameTaken = table.Any(kvp =>
                kvp.Key != Context.ConnectionId &&
                String.Equals(kvp.Value.Name, name, StringComparison.OrdinalIgnoreCase));
    
            if (nameTaken)
                throw new HubException("That display name is already in use.");
    
            if (table.TryGetValue(Context.ConnectionId, out WaitingPlayer? existing))
                joinedAt = existing.JoinedDate;
    
            table[Context.ConnectionId] = new WaitingPlayer(Context.ConnectionId, name, joinedAt);
        }
    
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
        }
        catch
        {
            // Roll back lobby entry if group join fails.
            table.TryRemove(Context.ConnectionId, out _);
            throw;
        }
    
        await BroadcastWaitingPlayers(tableId);
    }

    public Task<List<WaitingPlayer>> GetWaitingPlayers(String tableId)
    {
        if (!WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? table))
            return Task.FromResult(new List<WaitingPlayer>());

        List<WaitingPlayer> players = table.Values
            .OrderBy(p => p.JoinedDate)
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

    public async Task LeaveLobby(String tableId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new HubException("Table id is required.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, tableId);

        if (WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? table))
        {
            if (table.TryRemove(Context.ConnectionId, out _))
            {
                await BroadcastWaitingPlayers(tableId);
            }

            if (table.IsEmpty)
            {
                WaitingByTable.TryRemove(tableId, out _);
            }
        }
    }
}
