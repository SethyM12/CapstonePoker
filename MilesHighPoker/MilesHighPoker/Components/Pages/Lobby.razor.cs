using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MilesHighPoker.Models;

namespace MilesHighPoker.Components.Pages;

public partial class Lobby : IAsyncDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private const String TableId = "table-1";
    private HubConnection? _hubConnection;
    private String? CurrentConnectionId { get; set; }

    private List<WaitingPlayer> WaitingPlayers { get; set; } = new();
    private String PendingName { get; set; } = String.Empty;
    private String NameError { get; set; } = String.Empty;
    private bool HasJoined { get; set; }
    private bool IsJoining { get; set; }

    // Invitation state - for initiator
    private HashSet<String> SelectedPlayerIds { get; set; } = new();
    private bool ShowInviteModal { get; set; }
    private String? CurrentInviteId { get; set; }
    private Dictionary<String, bool?> InviteResponses { get; set; } = new();
    private int TotalInvitesSent { get; set; }

    // Invitation state - for invited players
    private class IncomingInvite
    {
        public String? InviteId { get; set; }
        public String? InitiatorName { get; set; }
        public int PlayerCount { get; set; }
    }

    private IncomingInvite? CurrentIncomingInvite { get; set; }

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
            WaitingPlayers = players.OrderBy(p => p.JoinedDate).ToList();
            _ = InvokeAsync(StateHasChanged);
        });

        // When you receive an invite
        _hubConnection.On<String, dynamic>("GameInviteReceived", (inviteId, invite) =>
        {
            String initiatorName = invite["initiatorName"];
            int invitedCount = ((List<String>)(invite["invitedConnectionIds"])).Count;

            CurrentIncomingInvite = new IncomingInvite
            {
                InviteId = inviteId,
                InitiatorName = initiatorName,
                PlayerCount = invitedCount
            };

            _ = InvokeAsync(StateHasChanged);
        });

        // When the initiator receives responses
        _hubConnection.On<String, dynamic>("InviteResponseReceived", (inviteId, response) =>
        {
            if (inviteId == CurrentInviteId)
            {
                String connectionId = response["respondentConnectionId"];
                Boolean accepted = response["accepted"];
                InviteResponses[connectionId] = accepted;
                _ = InvokeAsync(StateHasChanged);
            }
        });

        // When all responses are collected
        _hubConnection.On<String, List<String>>("AllInvitesResolved", (inviteId, acceptedPlayers) =>
        {
            if (inviteId == CurrentInviteId)
            {
                if (acceptedPlayers.Count > 0)
                {
                    _ = StartGameWithPlayers(acceptedPlayers);
                }
                else
                {
                    NameError = "No players accepted your invite.";
                    ShowInviteModal = false;
                    _ = InvokeAsync(StateHasChanged);
                }
            }
        });

        // When initiator cancels
        _hubConnection.On<String>("InviteCancelled", (inviteId) =>
        {
            if (CurrentIncomingInvite?.InviteId == inviteId)
            {
                CurrentIncomingInvite = null;
                _ = InvokeAsync(StateHasChanged);
            }
        });

        await _hubConnection.StartAsync();
        CurrentConnectionId = _hubConnection.ConnectionId;
        await _hubConnection.InvokeAsync("JoinTable", TableId);

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

        if (WaitingPlayers.Any(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            NameError = "That display name is already in use.";
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

    private void TogglePlayerSelection(String connectionId)
    {
        if (SelectedPlayerIds.Contains(connectionId))
        {
            SelectedPlayerIds.Remove(connectionId);
        }
        else if (SelectedPlayerIds.Count < 4)
        {
            SelectedPlayerIds.Add(connectionId);
        }
    }

    private bool IsPlayerSelected(String connectionId) => SelectedPlayerIds.Contains(connectionId);

    private bool CanSelectMore => SelectedPlayerIds.Count < 4;

    private async Task SendInvitesAsync()
    {
        if (_hubConnection is null || !HasJoined || SelectedPlayerIds.Count == 0)
            return;

        try
        {
            List<String> invitedIds = SelectedPlayerIds.ToList();
            CurrentInviteId = Guid.NewGuid().ToString();
            InviteResponses = invitedIds.ToDictionary(id => id, _ => (bool?)null);
            TotalInvitesSent = invitedIds.Count;
            ShowInviteModal = true;

            await _hubConnection.InvokeAsync("SendGameInvite", TableId, invitedIds);
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
    }

    private async Task AcceptInviteAsync()
    {
        if (_hubConnection is null || CurrentIncomingInvite?.InviteId is null)
            return;

        try
        {
            await _hubConnection.InvokeAsync("RespondToInvite", TableId, CurrentIncomingInvite.InviteId, true);
            CurrentIncomingInvite = null;
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
    }

    private async Task DeclineInviteAsync()
    {
        if (_hubConnection is null || CurrentIncomingInvite?.InviteId is null)
            return;

        try
        {
            await _hubConnection.InvokeAsync("RespondToInvite", TableId, CurrentIncomingInvite.InviteId, false);
            CurrentIncomingInvite = null;
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
    }

    private async Task StartGameWithPlayers(List<String> acceptedConnectionIds)
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected && HasJoined)
        {
            await _hubConnection.InvokeAsync("LeaveLobby", TableId);
        }

        String playerList = String.Join(",", acceptedConnectionIds);
        NavigationManager.NavigateTo($"/Game?players={playerList}");
    }

    private async Task EnterGame()
    {
        if (_hubConnection is not null && _hubConnection.State == HubConnectionState.Connected && HasJoined)
        {
            await _hubConnection.InvokeAsync("LeaveLobby", TableId);
        }

        NavigationManager.NavigateTo("/Game");
    }

    private void CloseInviteModal()
    {
        ShowInviteModal = false;
        SelectedPlayerIds.Clear();
        InviteResponses.Clear();
        CurrentInviteId = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
            await _hubConnection.DisposeAsync();
    }
}
