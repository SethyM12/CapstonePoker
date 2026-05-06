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

    // Use TableIdQuery if provided; otherwise redirect back
    private String ResolvedTableId =>
        String.IsNullOrWhiteSpace(TableIdQuery) ? String.Empty : TableIdQuery.Trim();

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
            StatusMessage = "Hand started.";
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

        short localSeat = GetLocalSeat(state);
        SeatSlots = BuildSeatSlots(state, localSeat);

        uiState = new UiTableState
        {
            TableId = state.TableId,
            IsHandRunning = state.IsHandRunning,
            Street = ParseStreet(state.Street),
            Pot = state.Pot,
            CurrentBet = state.CurrentBet,
            DealerSeat = state.DealerSeat,
            CurrentTurnSeat = state.CurrentTurnSeat,
            LocalSeat = localSeat,
            CommunityCards = state.CommunityCards.Select(ParseCard).ToArray(),
            LocalHoleCards = new Card?[2],
            Seats = SeatSlots.Where(seat => seat is not null).Select(seat => seat!).ToList()
        };
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
                1 => 3, // bottom-right (next clockwise)
                2 => 1, // top-right
                3 => 0, // top-left
                4 => 2, // bottom-left
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
            HoleCards = new Card?[2]
        };
    }

    private async Task SubmitActionAsync(String action, uint? totalBet = null)
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
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