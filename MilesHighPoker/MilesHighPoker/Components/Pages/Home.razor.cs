using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MilesHighPoker.GameLogic;
using MilesHighPoker.Hubs;
using MilesHighPoker.Models;

namespace MilesHighPoker.Components.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "tableId")]
    public String? TableIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "name")]
    public String? DisplayNameQuery { get; set; }

    private HubConnection? hubConnection;
    private bool hubStarted;

    private UiTableState uiState = new();
    private UiSeatState?[] SeatSlots { get; set; } = new UiSeatState?[5];

    private String StatusMessage { get; set; } = "Connecting...";
    private bool isStartingHand;
    private Card?[] localHoleCards = new Card?[2];
    
    private String raiseInput = ""; // bound to the numeric input
    private uint MinTotalBet => uiState.CurrentBet + uiState.MinimumRaise;
    
    private uint MaxTotalBet
    {
        get
        {
            // local seat is at SeatSlots[4] (your code keeps that mapping)
            UiSeatState? local = SeatSlots.Length > 4 ? SeatSlots[4] : null;
            if (local == null)
                return MinTotalBet;
            // player's maximum total bet for the street is their existing bet + chips (all-in)
            return local.Bet + local.Chips;
        }
    }

    // Use TableIdQuery if provided; otherwise redirect back
    private String ResolvedTableId =>
        String.IsNullOrWhiteSpace(TableIdQuery) ? String.Empty : TableIdQuery.Trim();

    private bool CanStartHand =>
        !uiState.IsHandRunning &&
        uiState.LocalSeat >= 0 &&
        uiState.LocalSeat == uiState.DealerSeat &&
        SeatSlots.Count(s => s is not null) >= 2;

    private bool IsLocalPlayerTurn =>
        uiState.IsHandRunning &&
        uiState.LocalSeat >= 0 &&
        uiState.LocalSeat == uiState.CurrentTurnSeat;

    private bool IsLocalPlayerDealer =>
        uiState.LocalSeat >= 0 &&
        uiState.LocalSeat == uiState.DealerSeat;
    
    private bool CanDealerAdvanceStreet =>
        uiState.IsHandRunning &&
        uiState.AwaitingDealerAdvance &&
        IsLocalPlayerDealer;

    private bool CanPlayerAct =>
        IsLocalPlayerTurn &&
        !uiState.AwaitingDealerAdvance;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || hubStarted)
            return;

        // Redirect if no tableId provided
        if (String.IsNullOrWhiteSpace(ResolvedTableId))
        {
            NavigationManager.NavigateTo("/");
            return;
        }

        hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/poker"))
            .WithAutomaticReconnect()
            .Build();

        hubConnection.On<String>("PlayerJoined", connectionId =>
        {
            Console.WriteLine($"Player joined: {connectionId}");

            _ = InvokeAsync(async () =>
            {
                await RefreshTableStateAsync();
                StateHasChanged();
            });
        });

        hubConnection.On<String, String, String>("PlayerActionReceived", (connectionId, action, result) =>
        {
            Console.WriteLine($"{connectionId} did {action}: {result}");
        });

        hubConnection.On("HandStarted", () =>
        {
            StatusMessage = "Hand started. Hole cards dealt.";
            _ = InvokeAsync(StateHasChanged);
        });

        // Handle per-client private hole cards
        hubConnection.On<List<CardDto>>("YourHoleCards", dtoList =>
        {
            localHoleCards = dtoList.Select(ParseCard).ToArray();
            StatusMessage = "Hole cards received.";
            _ = InvokeAsync(StateHasChanged);
        });

        hubConnection.On<TableStateDto?>("TableStateUpdated", state =>
        {
            ApplyTableState(state);
            _ = InvokeAsync(StateHasChanged);
        });

        hubConnection.Reconnected += async _ =>
        {
            StatusMessage = "Reconnected.";

            if (hubConnection is not null)
            {
                await JoinTableAsync();
                await RefreshTableStateAsync();
            }
        };

        hubConnection.Closed += error =>
        {
            StatusMessage = error is null
                ? "Disconnected."
                : $"Connection closed: {error.Message}";

            _ = InvokeAsync(StateHasChanged);
            return Task.CompletedTask;
        };

        await hubConnection.StartAsync();

        StatusMessage = "Connected.";
        await JoinTableAsync();
        await RefreshTableStateAsync();

        hubStarted = true;
    }

    private async Task JoinTableAsync()
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
            return;

        if (!String.IsNullOrWhiteSpace(DisplayNameQuery))
        {
            String displayName = DisplayNameQuery.Trim();

            try
            {
                await hubConnection.InvokeAsync("JoinGame", ResolvedTableId, displayName);
                StatusMessage = $"Joined game as {displayName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = ex.Message;
            }
        }
    }

    private async Task RefreshTableStateAsync()
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
            return;

        TableStateDto? state =
            await hubConnection.InvokeAsync<TableStateDto?>("GetTableState", ResolvedTableId);

        ApplyTableState(state);
    }

    private void ApplyTableState(TableStateDto? state)
    {
        if (state is null)
        {
            uiState = new UiTableState
            {
                TableId = ResolvedTableId
            };

            SeatSlots = new UiSeatState?[5];
            return;
        }

        // preserve whatever local hole cards we have in uiState before we overwrite
        Card?[] previousLocalHoleCards = localHoleCards;

        short localSeat = GetLocalSeat(state);
        SeatSlots = BuildSeatSlots(state, localSeat);

        uiState = new UiTableState
        {
            TableId = state.TableId,
            IsHandRunning = state.IsHandRunning,
            Street = ParseStreet(state.Street),
            Pot = state.Pot,
            CurrentBet = state.CurrentBet,
            MinimumRaise = state.MinimumRaise,
            DealerSeat = state.DealerSeat,
            CurrentTurnSeat = state.CurrentTurnSeat,
            LocalSeat = localSeat,
            CommunityCards = state.CommunityCards.Select(ParseCard).ToArray(),
            LocalHoleCards = GetLocalHoleCards(state, localSeat, previousLocalHoleCards),
            Seats = SeatSlots.Where(seat => seat is not null).Select(seat => seat!).ToList(),
            AwaitingDealerAdvance = state.AwaitingDealerAdvance  // ADD THIS
        };
    }
    
    private async Task AdvanceStreetAsync()
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
        {
            StatusMessage = "Not connected to server.";
            return;
        }

        if (!CanDealerAdvanceStreet)
        {
            StatusMessage = "Cannot advance street right now.";
            return;
        }

        try
        {
            await hubConnection.InvokeAsync("AdvanceStreet", ResolvedTableId);
            await RefreshTableStateAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error advancing street: {ex.Message}";
            await InvokeAsync(StateHasChanged);
        }
    }

    private Card?[] GetLocalHoleCards(TableStateDto state, short localSeat, Card?[]? previousLocalHoleCards = null)
    {
        if (previousLocalHoleCards is not null && previousLocalHoleCards.Any(card => card is not null))
            return previousLocalHoleCards;

        if (localSeat < 0)
            return new Card?[2];

        PlayerStateDto? localDto = state.Players.FirstOrDefault(player => player.Seat == localSeat);
        if (localDto != null && localDto.HoleCards != null && localDto.HoleCards.Count > 0)
        {
            return localDto.HoleCards.Select(ParseCard).ToArray();
        }

        int holeCardCount = state.Players.FirstOrDefault(player => player.Seat == localSeat)?.HoleCardCount ?? 2;
        return new Card?[holeCardCount];
    }

    private short GetLocalSeat(TableStateDto state)
    {
        if (hubConnection?.ConnectionId is null)
            return -1;

        PlayerStateDto? localPlayer = state.Players.FirstOrDefault(player =>
            String.Equals(player.ConnectionId, hubConnection.ConnectionId, StringComparison.Ordinal));

        return localPlayer?.Seat ?? -1;
    }

    private static HandStreet ParseStreet(String street)
    {
        if (Enum.TryParse(street, ignoreCase: true, out HandStreet parsedStreet))
            return parsedStreet;

        return HandStreet.PreDeal;
    }

    private static Card? ParseCard(CardDto? dto)
    {
        if (dto is null)
            return null;

        if (!Enum.TryParse(dto.Rank, ignoreCase: true, out CardRank rank))
            return null;

        if (!Enum.TryParse(dto.Suit, ignoreCase: true, out CardSuit suit))
            return null;

        return new Card
        {
            Rank = rank,
            Suit = suit
        };
    }
    
    private UiSeatState?[] BuildSeatSlots(TableStateDto state, short localSeat)
    {
        UiSeatState?[] slots = new UiSeatState?[Table.MAX_PLAYERS];
        List<PlayerStateDto> players = state.Players.OrderBy(player => player.Seat).ToList();
        
        int OffsetToSlotIndex(int offset)
        {
            return offset switch
            {
                0 => 4, // local/current
                1 => 2, // (next clockwise)
                2 => 0,
                3 => 1,
                4 => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(offset))
            };
        }
    
        if (localSeat >= 0)
        {
            PlayerStateDto? localPlayer = players.FirstOrDefault(p => p.Seat == localSeat);
            if (localPlayer is not null)
            {
                slots[OffsetToSlotIndex(0)] = BuildSeatState(localPlayer, true);
            }
    
            foreach (PlayerStateDto other in players.Where(p => p.Seat != localSeat))
            {
                int offset = GetRelativeSeatDistance(other.Seat, localSeat); // 1..(MAX_PLAYERS-1)
                int slotIndex = OffsetToSlotIndex(offset);
                slots[slotIndex] = BuildSeatState(other, false);
            }
        }
        else
        {
            // If we don't know the local seat (spectator), just show players in seat order
            for (int i = 0; i < players.Count && i < Table.MAX_PLAYERS; i++)
            {
                slots[i] = BuildSeatState(players[i], false);
            }
        }
    
        return slots;
    }

    private static int GetRelativeSeatDistance(short seat, short originSeat)
    {
        int distance = seat - originSeat;
        if (distance < 0)
        {
            distance += Table.MAX_PLAYERS;
        }

        return distance;
    }

    private UiSeatState BuildSeatState(PlayerStateDto player, bool isLocalPlayer)
    {
        return new UiSeatState
        {
            Seat = player.Seat,
            ConnectionId = player.ConnectionId,
            Name = player.Name,
            Chips = player.Chips,
            Bet = player.Bet,
            Folded = player.Folded,
            IsAllIn = player.IsAllIn,
            IsLocalPlayer = isLocalPlayer,
            HoleCards = player.HoleCards != null && player.HoleCards.Count > 0
                ? player.HoleCards.Select(ParseCard).ToArray()
                : new Card?[2],
            IsWinner = player.IsWinner
        };
    }

    private async Task StartHandAsync()
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
        {
            StatusMessage = "Not connected to server.";
            return;
        }

        if (!CanStartHand)
        {
            StatusMessage = "Cannot start hand right now.";
            return;
        }

        isStartingHand = true;
        StatusMessage = "Dealing...";
    
        Console.WriteLine("Client: About to call StartHand hub method");

        try
        {
            await hubConnection.InvokeAsync("StartHand", ResolvedTableId, (short)-1);
            Console.WriteLine("Client: StartHand hub call succeeded");
            await RefreshTableStateAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client: StartHand hub call failed: {ex.Message}");
            StatusMessage = $"Error starting hand: {ex.Message}";
        }
        finally
        {
            isStartingHand = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SubmitActionAsync(String action, uint? totalBet = null)
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
            return;

        if (!CanPlayerAct)
            return;

        try
        {
            await hubConnection.InvokeAsync("SubmitAction", ResolvedTableId, action, totalBet);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task FoldAsync()
    {
        return SubmitActionAsync(nameof(PlayerAction.Fold));
    }

    private Task CheckAsync()
    {
        return SubmitActionAsync(nameof(PlayerAction.Check));
    }

    private Task CallAsync()
    {
        return SubmitActionAsync(nameof(PlayerAction.Call));
    }

    private Task RaiseAsync(uint totalBet)
    {
        return SubmitActionAsync(nameof(PlayerAction.Raise), totalBet);
    }
    
private async Task RaiseClickedAsync()
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
        {
            StatusMessage = "Not connected to server.";
            await InvokeAsync(StateHasChanged);
            return;
        }
        
        if (!IsLocalPlayerTurn)
        {
            StatusMessage = "Not your turn.";
            await InvokeAsync(StateHasChanged);
            return;
        }
    
        if (!uint.TryParse(raiseInput, out uint requestedTotalBet))
        {
            StatusMessage = "Enter a numeric raise total (e.g. 150).";
            await InvokeAsync(StateHasChanged);
            return;
        }
    
        // enforce minimum total bet: CurrentBet + MinimumRaise
        uint minTotal = MinTotalBet;
        if (requestedTotalBet < minTotal)
            requestedTotalBet = minTotal;
    
        // enforce maximum total bet (player's bet + chips)
        uint maxTotal = MaxTotalBet;
        if (requestedTotalBet > maxTotal)
            requestedTotalBet = maxTotal;
    
        try
        {
            await RaiseAsync(requestedTotalBet);

            // on success, clear the input
            raiseInput = String.Empty;
            StatusMessage = $"Raised to {requestedTotalBet}.";
        }
        catch (Exception ex)
        {
            // preserve the typed value on error so the user can retry/adjust
            StatusMessage = $"Raise failed: {ex.Message}";
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private static String ToCardFile(Card card)
    {
        String rank = card.Rank switch
        {
            CardRank.Two => "2",
            CardRank.Three => "3",
            CardRank.Four => "4",
            CardRank.Five => "5",
            CardRank.Six => "6",
            CardRank.Seven => "7",
            CardRank.Eight => "8",
            CardRank.Nine => "9",
            CardRank.Ten => "10",
            CardRank.Jack => "jack",
            CardRank.Queen => "queen",
            CardRank.King => "king",
            CardRank.Ace => "ace",
            _ => throw new ArgumentOutOfRangeException()
        };

        String suit = card.Suit switch
        {
            CardSuit.Clubs => "clubs",
            CardSuit.Diamonds => "diamonds",
            CardSuit.Hearts => "hearts",
            CardSuit.Spades => "spades",
            _ => throw new ArgumentOutOfRangeException()
        };

        return $"/images/cards/{rank}_of_{suit}.png";
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }
}