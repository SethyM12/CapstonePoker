using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MilesHighPoker.Models;

namespace MilesHighPoker.Components.Pages;

public partial class Lobby : IAsyncDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private const String TableId = "table-1";
    private HubConnection? _hubConnection;

    private List<WaitingPlayer> WaitingPlayers { get; set; } = new();
    private String PendingName { get; set; } = String.Empty;
    private String NameError { get; set; } = String.Empty;
    private bool HasJoined { get; set; }
    private bool IsJoining { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/poker"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<List<WaitingPlayer>>("WaitingPlayersUpdated", players =>
        {
            WaitingPlayers = players.OrderBy(p => p.Joined).ToList();
            _ = InvokeAsync(StateHasChanged);
        });

        await _hubConnection.StartAsync();

        WaitingPlayers = await _hubConnection.InvokeAsync<List<WaitingPlayer>>("GetWaitingPlayers", TableId);
        await InvokeAsync(StateHasChanged);
    }

    private async Task JoinLobbyAsync()
    {
        NameError = String.Empty;
        String name = (PendingName ?? String.Empty).Trim();

        if (name.Length < 2 || name.Length > 20)
        {
            NameError = "Name must be 2-20 characters.";
            return;
        }

        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
        {
            NameError = "Connection is not ready. Try again.";
            return;
        }

        IsJoining = true;
        try
        {
            await _hubConnection.InvokeAsync("SetDisplayName", TableId, name);
            PendingName = name;
            HasJoined = true;
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
        finally
        {
            IsJoining = false;
        }
    }

    private void EnterGame()
    {
        NavigationManager.NavigateTo("/Game");
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}
