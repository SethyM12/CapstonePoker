using Microsoft.AspNetCore.SignalR;

namespace MilesHighPoker.Hubs;

public class PokerHub : Hub
{
    public async Task JoinTable(String tableId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
        await Clients.Group(tableId).SendAsync("PlayerJoined", Context.ConnectionId);
    }

    public async Task SendAction(String tableId, String action)
    {
        await Clients.Group(tableId).SendAsync("PlayerActionReceived", Context.ConnectionId, action);
    }

    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"Client connected: {Context.ConnectionId}");
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"Client disconnected: {Context.ConnectionId}");
        return base.OnDisconnectedAsync(exception);
    }
}