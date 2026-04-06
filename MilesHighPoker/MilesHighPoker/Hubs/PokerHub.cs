using Microsoft.AspNetCore.SignalR;

namespace MilesHighPoker.Hubs;

public class PokerHub : Hub
{
    public override Task OnConnectedAsync()
    {
        Console.WriteLine($"{Context.ConnectionId} connected");
        return base.OnConnectedAsync();
    }
}