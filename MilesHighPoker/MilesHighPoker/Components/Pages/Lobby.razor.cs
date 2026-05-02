using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MilesHighPoker.Hubs;
using MilesHighPoker.Models;

namespace MilesHighPoker.Components.Pages;

public partial class Lobby : IAsyncDisposable
{
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private const String TableId = "table-1";
    private HubConnection? HubConnection;
    private String? CurrentConnectionId { get; set; }

    private List<WaitingPlayer> WaitingPlayers { get; set; } = new();
    private String PendingName { get; set; } = String.Empty;
    private String NameError { get; set; } = String.Empty;
    private bool HasJoined { get; set; }
    private bool IsJoining { get; set; }

    private HashSet<String> SelectedPlayerIds { get; set; } = new();
    private bool ShowInviteModal { get; set; }
    private String? CurrentInviteId { get; set; }
    private Dictionary<String, bool?> InviteResponses { get; set; } = new();

    private sealed class IncomingInvite
    {
        public String InviteId { get; set; } = String.Empty;
        public String InitiatorName { get; set; } = String.Empty;
        public int PlayerCount { get; set; }
    }

    private IncomingInvite? CurrentIncomingInvite { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        HubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/poker"))
            .WithAutomaticReconnect()
            .Build();

        HubConnection.On<List<WaitingPlayer>>("WaitingPlayersUpdated", players =>
        {
            WaitingPlayers = players.OrderBy(p => p.JoinedDate).ToList();
            _ = InvokeAsync(StateHasChanged);
        });

        HubConnection.On<String>("InvitesSent", inviteId =>
        {
            CurrentInviteId = inviteId;
            _ = InvokeAsync(StateHasChanged);
        });

        HubConnection.On<String, GameInviteDto>("GameInviteReceived", (inviteId, invite) =>
        {
            CurrentIncomingInvite = new IncomingInvite
            {
                InviteId = inviteId,
                InitiatorName = invite.InitiatorName,
                PlayerCount = invite.InvitedConnectionIds.Count + 1
            };

            _ = InvokeAsync(StateHasChanged);
        });

        HubConnection.On<String, InviteResponseDto>("InviteResponseReceived", (inviteId, response) =>
        {
            if (inviteId != CurrentInviteId)
                return;

            if (!String.IsNullOrWhiteSpace(response.RespondentConnectionId))
            {
                InviteResponses[response.RespondentConnectionId] = response.Accepted;
                _ = InvokeAsync(StateHasChanged);
            }
        });

        HubConnection.On<String, List<String>>("AllInvitesResolved", (inviteId, acceptedPlayers) =>
        {
            if (inviteId != CurrentInviteId)
                return;

            if (acceptedPlayers.Count > 0)
            {
                _ = StartGameWithPlayers(acceptedPlayers);
            }
            else
            {
                NameError = "No players accepted your invite.";
                ResetOutgoingInviteState();
                _ = InvokeAsync(StateHasChanged);
            }
        });

        HubConnection.On<String>("InviteCancelled", inviteId =>
        {
            if (CurrentIncomingInvite?.InviteId == inviteId)
            {
                CurrentIncomingInvite = null;
                _ = InvokeAsync(StateHasChanged);
            }
        });

        HubConnection.Reconnected += async connectionId =>
        {
            CurrentConnectionId = connectionId;

            if (HubConnection is not null)
            {
                await HubConnection.InvokeAsync("JoinTable", TableId);

                if (HasJoined && !String.IsNullOrWhiteSpace(PendingName))
                {
                    try
                    {
                        await HubConnection.InvokeAsync("SetDisplayName", TableId, PendingName);
                    }
                    catch (Exception ex)
                    {
                        NameError = ex.Message;
                    }
                }

                WaitingPlayers = await HubConnection.InvokeAsync<List<WaitingPlayer>>("GetWaitingPlayers", TableId);
                await InvokeAsync(StateHasChanged);
            }
        };

        await HubConnection.StartAsync();
        CurrentConnectionId = HubConnection.ConnectionId;

        await HubConnection.InvokeAsync("JoinTable", TableId);
        WaitingPlayers = await HubConnection.InvokeAsync<List<WaitingPlayer>>("GetWaitingPlayers", TableId);
        await InvokeAsync(StateHasChanged);
    }

    private async Task JoinLobbyAsync()
    {
        NameError = String.Empty;
        String name = PendingName.Trim();

        if (name.Length < 2 || name.Length > 20)
        {
            NameError = "Name must be 2-20 characters.";
            return;
        }

        if (HubConnection is null || HubConnection.State != HubConnectionState.Connected)
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
            await HubConnection.InvokeAsync("SetDisplayName", TableId, name);
            PendingName = name;
            HasJoined = true;
            CurrentConnectionId = HubConnection.ConnectionId;
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
        if (HubConnection is null || !HasJoined || SelectedPlayerIds.Count == 0)
            return;

        try
        {
            List<String> invitedIds = SelectedPlayerIds.ToList();

            InviteResponses = invitedIds.ToDictionary(id => id, _ => (bool?)null);
            CurrentInviteId = null;
            ShowInviteModal = true;

            await HubConnection.InvokeAsync("SendGameInvite", TableId, invitedIds);
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
            ResetOutgoingInviteState();
        }
    }

    private async Task AcceptInviteAsync()
    {
        if (HubConnection is null || CurrentIncomingInvite?.InviteId is null)
            return;

        try
        {
            await HubConnection.InvokeAsync("RespondToInvite", TableId, CurrentIncomingInvite.InviteId, true);
            CurrentIncomingInvite = null;
            await LeaveLobbyAndNavigateToGameAsync();
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
    }

    private async Task DeclineInviteAsync()
    {
        if (HubConnection is null || CurrentIncomingInvite?.InviteId is null)
            return;

        try
        {
            await HubConnection.InvokeAsync("RespondToInvite", TableId, CurrentIncomingInvite.InviteId, false);
            CurrentIncomingInvite = null;
        }
        catch (Exception ex)
        {
            NameError = ex.Message;
        }
    }

    private async Task StartGameWithPlayers(List<String> acceptedConnectionIds)
    {
        ResetOutgoingInviteState();

        if (acceptedConnectionIds.Count == 0)
        {
            NameError = "No players accepted your invite.";
            await InvokeAsync(StateHasChanged);
            return;
        }

        List<String> playerIds = new();

        if (!String.IsNullOrWhiteSpace(CurrentConnectionId))
            playerIds.Add(CurrentConnectionId);

        playerIds.AddRange(
            acceptedConnectionIds.Where(id => !String.IsNullOrWhiteSpace(id))
        );

        playerIds = playerIds
            .Distinct(StringComparer.Ordinal)
            .ToList();

        String playerList = String.Join(",", playerIds);

        await LeaveLobbyIfJoinedAsync();
        NavigationManager.NavigateTo($"/Game?players={playerList}");
    }

    private async Task EnterGame()
    {
        await LeaveLobbyIfJoinedAsync();
        NavigationManager.NavigateTo("/Game");
    }

    private void CloseInviteModal()
    {
        ResetOutgoingInviteState();
    }

    private void ResetOutgoingInviteState()
    {
        ShowInviteModal = false;
        SelectedPlayerIds.Clear();
        InviteResponses.Clear();
        CurrentInviteId = null;
    }

    private async Task LeaveLobbyIfJoinedAsync()
    {
        if (HubConnection is null || HubConnection.State != HubConnectionState.Connected || !HasJoined)
            return;

        try
        {
            await HubConnection.InvokeAsync("LeaveLobby", TableId);
        }
        catch
        {
            // Best effort only; navigation should still continue.
        }
        finally
        {
            HasJoined = false;
        }
    }

    private async Task LeaveLobbyAndNavigateToGameAsync()
    {
        await LeaveLobbyIfJoinedAsync();
        NavigationManager.NavigateTo("/Game");
    }

    public async ValueTask DisposeAsync()
    {
        if (HubConnection is not null)
        {
            await LeaveLobbyIfJoinedAsync();
            await HubConnection.DisposeAsync();
        }
    }
}