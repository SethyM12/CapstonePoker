using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Components.Pages;

public partial class Home : IAsyncDisposable
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private HubConnection? _hubConnection;
    private bool _hubStarted;
    private const String TableId = "table-1";

    private static readonly String CardBackPath = "/images/cards/card_back.png";

    private Card?[] CommunityCards { get; set; } = new Card[5];

    private Card?[] PlayerHand { get; set; } = new Card[2];

    private uint Pot { get; set; } = 0;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _hubStarted)
            return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(NavigationManager.ToAbsoluteUri("/hubs/poker"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<String>("PlayerJoined", connectionId =>
        {
            Console.WriteLine($"Player joined: {connectionId}");
        });
        
        _hubConnection.On<String, String>("PlayerActionReceived", (connectionId, action) =>
        {
            Console.WriteLine($"{connectionId} did {action}");
        });

        _hubConnection.Reconnected += async _ =>
        {
            Console.WriteLine("Reconnected; rejoining table...");
            if (_hubConnection is not null)
                await _hubConnection.InvokeAsync("JoinTable", TableId);
        };

        _hubConnection.Closed += error =>
        {
            Console.WriteLine($"Connection closed: {error?.Message}");
            return Task.CompletedTask;
        };

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinTable", TableId);

        _hubStarted = true;
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

        return $"images/cards/{rank}_of_{suit}.png";
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
