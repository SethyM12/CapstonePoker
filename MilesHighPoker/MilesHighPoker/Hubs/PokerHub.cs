using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MilesHighPoker.GameLogic;
using MilesHighPoker.Models;
using MilesHighPoker.Services;

namespace MilesHighPoker.Hubs;

public sealed record TableStateDto(
    String TableId,
    Boolean IsHandRunning,
    String Street,
    uint Pot,
    uint CurrentBet,
    short DealerSeat,
    short CurrentTurnSeat,
    List<PlayerStateDto> Players,
    List<CardDto?> CommunityCards
);

public sealed record PlayerStateDto(
    String ConnectionId,
    String Name,
    short Seat,
    uint Chips,
    uint Bet,
    Boolean Folded,
    Boolean IsAllIn,
    int HoleCardCount
);

public sealed record CardDto(
    String Rank, 
    String Suit
);

public sealed record GameInviteDto(
    String InitiatorConnectionId,
    String InitiatorName,
    List<String> InvitedConnectionIds
);

public sealed record InviteResponseDto(
    String RespondentConnectionId,
    String RespondentName,
    Boolean Accepted
);

public sealed class PokerHub : Hub
{
    private readonly GameManager gameManager;

    // Lobby-only state (display names before users sit in a game seat).
    private static readonly ConcurrentDictionary<String, ConcurrentDictionary<String, WaitingPlayer>> WaitingByTable
        = new(StringComparer.Ordinal);

    // Tracks the last joined table per connection for disconnect cleanup.
    private static readonly ConcurrentDictionary<String, String> ConnectionTable
        = new(StringComparer.Ordinal);
    
    private static readonly ConcurrentDictionary<String, GameInviteDto> ActiveInvites
        = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<String, ConcurrentDictionary<String, Boolean>> InvitedPlayersResponses
        = new(StringComparer.Ordinal);

    public PokerHub(GameManager gameManager)
    {
        this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
    }

    #region Lobby methods

    public async Task JoinTable(String tableId)
    {
        EnsureTableId(tableId);

        ConnectionTable[Context.ConnectionId] = tableId;
        gameManager.GetOrCreateTable(tableId);

        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
        await BroadcastWaitingPlayers(tableId);
        await BroadcastTableState(tableId);
    }

    public async Task SetDisplayName(String tableId, String name)
    {
        EnsureTableId(tableId);

        name = (name ?? String.Empty).Trim();
        if (name.Length < 2 || name.Length > 20)
            throw new HubException("Name must be 2-20 characters.");

        ConcurrentDictionary<String, WaitingPlayer> lobby = WaitingByTable.GetOrAdd(
            tableId,
            _ => new ConcurrentDictionary<String, WaitingPlayer>(StringComparer.Ordinal)
        );

        DateTime joinedAt = DateTime.UtcNow;

        // Atomic uniqueness check by name within the table lobby.
        lock (lobby)
        {
            Boolean nameTaken = lobby.Any(kvp =>
                kvp.Key != Context.ConnectionId &&
                String.Equals(kvp.Value.Name, name, StringComparison.OrdinalIgnoreCase));

            if (nameTaken)
                throw new HubException("That display name is already in use.");

            if (lobby.TryGetValue(Context.ConnectionId, out WaitingPlayer? existing))
                joinedAt = existing.JoinedDate;

            lobby[Context.ConnectionId] = new WaitingPlayer(Context.ConnectionId, name, joinedAt);
        }

        ConnectionTable[Context.ConnectionId] = tableId;
        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);
        await BroadcastWaitingPlayers(tableId);
    }

    public Task<List<WaitingPlayer>> GetWaitingPlayers(String tableId)
    {
        EnsureTableId(tableId);

        if (!WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
            return Task.FromResult(new List<WaitingPlayer>());

        List<WaitingPlayer> players = lobby.Values
            .OrderBy(p => p.JoinedDate)
            .ToList();

        return Task.FromResult(players);
    }

    public async Task LeaveLobby(String tableId)
    {
        EnsureTableId(tableId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, tableId);

        if (WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
        {
            if (lobby.TryRemove(Context.ConnectionId, out _))
                await BroadcastWaitingPlayers(tableId);

            if (lobby.IsEmpty)
                WaitingByTable.TryRemove(tableId, out _);
        }
    }
    
    #endregion

    #region Game Methods

    public async Task JoinGame(String tableId, String displayName)
    {
        EnsureTableId(tableId);

        displayName = (displayName ?? String.Empty).Trim();
        if (displayName.Length < 2 || displayName.Length > 20)
            throw new HubException("Name must be 2-20 characters.");

        uint playerId = (uint)Math.Abs(Context.ConnectionId.GetHashCode());

        Boolean joined = gameManager.TryJoinGame(tableId, displayName, playerId, Context.ConnectionId);
        if (!joined)
            throw new HubException("Unable to join game. Table may be full or hand may be running.");

        // If user sat down, remove them from waiting list.
        if (WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
        {
            lobby.TryRemove(Context.ConnectionId, out _);
            if (lobby.IsEmpty)
                WaitingByTable.TryRemove(tableId, out _);
        }

        ConnectionTable[Context.ConnectionId] = tableId;
        await Groups.AddToGroupAsync(Context.ConnectionId, tableId);

        await Clients.Group(tableId).SendAsync("PlayerJoined", Context.ConnectionId);
        await BroadcastWaitingPlayers(tableId);
        await BroadcastTableState(tableId);
    }

    public async Task StartHand(String tableId, short dealerSeat = 0)
    {
        EnsureTableId(tableId);

        Boolean started = gameManager.TryStartHand(tableId, dealerSeat);
        if (!started)
            throw new HubException("Unable to start hand. Need at least 2 players with chips and no active hand.");

        await Clients.Group(tableId).SendAsync("HandStarted");
        await BroadcastTableState(tableId);
    }

    public async Task SubmitAction(String tableId, String action, uint? totalBet = null)
    {
        EnsureTableId(tableId);

        if (!gameManager.TryGetTable(tableId, out Table? table) || table == null)
            throw new HubException("Table not found.");

        Player? actor = table.GetPlayer(Context.ConnectionId);
        if (actor == null)
            throw new HubException("You are not seated in this game.");

        if (!Enum.TryParse(action, ignoreCase: true, out PlayerAction parsedAction))
            throw new HubException("Unknown action.");

        TurnStepResult result = gameManager.ProcessAction(tableId, actor.Seat, parsedAction, totalBet);

        await Clients.Group(tableId).SendAsync("PlayerActionReceived", Context.ConnectionId, action, result.ToString());
        await BroadcastTableState(tableId);
    }

    public Task<TableStateDto?> GetTableState(String tableId)
    {
        EnsureTableId(tableId);

        if (!gameManager.TryGetTable(tableId, out Table? table) || table == null)
            return Task.FromResult<TableStateDto?>(null);

        return Task.FromResult<TableStateDto?>(BuildTableState(table));
    }

    public async Task LeaveGame(String tableId)
    {
        EnsureTableId(tableId);

        gameManager.TryLeaveGame(tableId, Context.ConnectionId);
        await BroadcastTableState(tableId);
    }
    
    #endregion
    
    #region Invitations

    public async Task SendGameInvite(String tableId, List<String> invitedConnectionIds)
    {
        EnsureTableId(tableId);

        if (invitedConnectionIds is null || invitedConnectionIds.Count == 0)
            throw new HubException("Must invite at least one player.");

        if (invitedConnectionIds.Count > 4)
            throw new HubException("Cannot invite more than 4 players.");

        if (invitedConnectionIds.Contains(Context.ConnectionId))
            throw new HubException("Cannot invite yourself.");

        if (!WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
            throw new HubException("No one is waiting at this table.");

        foreach (String connectionId in invitedConnectionIds)
        {
            if (!lobby.ContainsKey(connectionId))
                throw new HubException("One or more invited players are not in the lobby.");
        }

        if (!lobby.TryGetValue(Context.ConnectionId, out WaitingPlayer? initiator))
            throw new HubException("Could not determine your display name.");

        String inviteId = Guid.NewGuid().ToString();

        GameInviteDto invite = new GameInviteDto(
            Context.ConnectionId,
            initiator.Name,
            invitedConnectionIds
        );

        ActiveInvites[inviteId] = invite;
        
        ConcurrentDictionary<String, Boolean> responseTracker = new(StringComparer.Ordinal);
        InvitedPlayersResponses[inviteId] = responseTracker;

        foreach (String connectionId in invitedConnectionIds)
        {
            await Clients.Client(connectionId).SendAsync("GameInviteReceived", inviteId, invite);
        }

        await Clients.Caller.SendAsync("InvitesSent", inviteId);
    }

    public async Task RespondToInvite(String tableId, String inviteId, Boolean accept)
    {
        EnsureTableId(tableId);

        if (!ActiveInvites.TryGetValue(inviteId, out GameInviteDto? invite) || invite is null)
            throw new HubException("Invite not found or expired.");

        if (!invite.InvitedConnectionIds.Contains(Context.ConnectionId))
            throw new HubException("This invite was not sent to you.");

        String initiatorConnectionId = invite.InitiatorConnectionId;

        if (!WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
            throw new HubException("Lobby state lost.");

        String respondentName = "Unknown";
        if (lobby.TryGetValue(Context.ConnectionId, out WaitingPlayer? responder))
            respondentName = responder.Name;

        InviteResponseDto response = new InviteResponseDto(
            Context.ConnectionId,
            respondentName,
            accept
        );

        await Clients.Client(initiatorConnectionId).SendAsync("InviteResponseReceived", inviteId, response);

        if (InvitedPlayersResponses.TryGetValue(inviteId, out ConcurrentDictionary<String, Boolean>? allResponses))
        {
            allResponses[Context.ConnectionId] = accept;

            if (allResponses.Count >= invite.InvitedConnectionIds.Count)
            {
                ActiveInvites.TryRemove(inviteId, out _);

                List<String> acceptedPlayers = allResponses
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                await Clients.Client(initiatorConnectionId).SendAsync("AllInvitesResolved", inviteId, acceptedPlayers);
                InvitedPlayersResponses.TryRemove(inviteId, out _);
            }
        }
    }

    public async Task CancelInvites(String tableId, String inviteId)
    {
        EnsureTableId(tableId);

        if (!ActiveInvites.TryGetValue(inviteId, out GameInviteDto? invite) || invite is null)
            throw new HubException("Invite not found.");

        if (invite.InitiatorConnectionId != Context.ConnectionId)
            throw new HubException("Only the initiator can cancel this invite.");

        ActiveInvites.TryRemove(inviteId, out _);
        InvitedPlayersResponses.TryRemove(inviteId, out _);

        foreach (String connectionId in invite.InvitedConnectionIds)
        {
            await Clients.Client(connectionId).SendAsync("InviteCancelled", inviteId);
        }
    }

    #endregion

    // Keeps your existing client call pattern usable while you migrate to SubmitAction.
    public async Task SendAction(String tableId, String action)
    {
        await SubmitAction(tableId, action, null);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionTable.TryRemove(Context.ConnectionId, out String? tableId) && !String.IsNullOrWhiteSpace(tableId))
        {
            if (WaitingByTable.TryGetValue(tableId, out ConcurrentDictionary<String, WaitingPlayer>? lobby))
            {
                if (lobby.TryRemove(Context.ConnectionId, out _))
                    await BroadcastWaitingPlayers(tableId);

                if (lobby.IsEmpty)
                    WaitingByTable.TryRemove(tableId, out _);
            }

            gameManager.TryLeaveGame(tableId, Context.ConnectionId);
            await BroadcastTableState(tableId);
        }
        else
        {
            // Fallback sweep if connection->table mapping is missing.
            foreach ((String existingTableId, ConcurrentDictionary<String, WaitingPlayer> lobby) in WaitingByTable)
            {
                if (lobby.TryRemove(Context.ConnectionId, out _))
                    await BroadcastWaitingPlayers(existingTableId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BroadcastWaitingPlayers(String tableId)
    {
        List<WaitingPlayer> players = await GetWaitingPlayers(tableId);
        await Clients.Group(tableId).SendAsync("WaitingPlayersUpdated", players);
    }

    private async Task BroadcastTableState(String tableId)
    {
        if (!gameManager.TryGetTable(tableId, out Table? table) || table == null)
        {
            await Clients.Group(tableId).SendAsync("TableStateUpdated", null);
            return;
        }

        TableStateDto dto = BuildTableState(table);
        await Clients.Group(tableId).SendAsync("TableStateUpdated", dto);
    }

    private static void EnsureTableId(String tableId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new HubException("Table id is required.");
    }

    private static TableStateDto BuildTableState(Table table)
    {
        GameState? state = table.CurrentGameState;

        List<PlayerStateDto> players = table.Players
            .OrderBy(p => p.Seat)
            .Select(p => new PlayerStateDto(
                p.ConnectionId,
                p.Name,
                p.Seat,
                p.Chips,
                p.Bet,
                p.Folded,
                p.IsAllIn,
                p.Cards.Count
            ))
            .ToList();

        List<CardDto?> communityCards = (state?.CommunityCards ?? new Card[5])
            .Select(c => c == null ? null : new CardDto(c.Rank.ToString(), c.Suit.ToString()))
            .ToList();

        return new TableStateDto(
            table.TableId,
            table.IsHandRunning,
            state?.CurrentStreet.ToString() ?? nameof(HandStreet.PreDeal),
            state?.Pot ?? 0,
            state?.CurrentBet ?? 0,
            state?.DealerPosition ?? 0,
            state?.CurrentPlayerPosition ?? 0,
            players,
            communityCards
        );
    }
}