namespace MilesHighPoker.Models;

public class Table
{
    public const short MAX_PLAYERS = 5;
    public const uint SMALL_BLIND = 5;
    public const uint BIG_BLIND = SMALL_BLIND * 2;
    public const uint MAX_BET = 150;
    public const uint STARTING_MONEY = 500;
    public String  TableId { get; }
    
    public List<WaitingPlayer> WaitingPlayers { get; }
    public List<Player> Players { get; }
    public bool IsHandRunning { get; private set; }
    public GameState? CurrentGameState { get; private set; }
    
    public Table(String tableId)
    {
        if (String.IsNullOrEmpty(tableId))
        {
            throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));
        }
        TableId = tableId;
        WaitingPlayers = new List<WaitingPlayer>();
        Players = new List<Player>();
        IsHandRunning = false;
        CurrentGameState = null;
    }
    
    public bool CanJoinTable => Players.Count < MAX_PLAYERS;
    public bool CanStartHand => !IsHandRunning && Players.Count >= 2;
    
    public bool AddWaitingPlayer(WaitingPlayer waitingPlayer)
    {
        if (String.IsNullOrWhiteSpace(waitingPlayer.Name))
            return false;

        // Check for duplicate ConnectionId
        if (WaitingPlayers.Any(w => w.ConnectionId == waitingPlayer.ConnectionId))
            return false;

        // Check for duplicate player names (case-insensitive)
        if (WaitingPlayers.Any(w => String.Equals(w.Name, waitingPlayer.Name, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (Players.Any(p => String.Equals(p.Name, waitingPlayer.Name, StringComparison.OrdinalIgnoreCase)))
            return false;

        WaitingPlayers.Add(waitingPlayer);
        return true;
    }

    public bool RemoveWaitingPlayer(WaitingPlayer waitingPlayer)
    {
        int index = WaitingPlayers.FindIndex(w => w.ConnectionId == waitingPlayer.ConnectionId);
        if (index < 0) return false;
        
        WaitingPlayers.RemoveAt(index);
        return true;
    }
    
    public bool TryAddPlayer(Player player)
    {
        if (!CanJoinTable)
            return false;

        if (Players.Any(p => p.ConnectionId == player.ConnectionId))
            return false;

        if (player.Seat < 0 || player.Seat >= MAX_PLAYERS)
            return false;

        if (Players.Any(p => p.Seat == player.Seat))
            return false;

        if (Players.Any(p => String.Equals(p.Name, player.Name, StringComparison.OrdinalIgnoreCase)))
            return false;

        Players.Add(player);
        return true;
    }
    
    public bool RemovePlayer(String connectionId)
    {
        Int32 index = Players.FindIndex(p => p.ConnectionId == connectionId);
        if (index < 0) return false;

        Players.RemoveAt(index);
        return true;
    }

    public Player? GetPlayer(String connectionId)
    {
        return Players.FirstOrDefault(p => p.ConnectionId == connectionId);
    }
    
    public void PostBlinds()
    {
        if (CurrentGameState == null)
            throw new InvalidOperationException("No game state available.");
        if (Players.Count < 2)
            throw new InvalidOperationException("Need at least 2 players to post blinds.");

        short smallBlindSeat = GetNextOccupiedSeat(CurrentGameState.DealerPosition);
        short bigBlindSeat = GetNextOccupiedSeat(smallBlindSeat);

        Player smallBlindPlayer = GetPlayerBySeat(smallBlindSeat);
        Player bigBlindPlayer = GetPlayerBySeat(bigBlindSeat);

        uint smallBlindPosted = smallBlindPlayer.PostBlind(SMALL_BLIND);
        uint bigBlindPosted = bigBlindPlayer.PostBlind(BIG_BLIND);

        CurrentGameState.AddToPot(smallBlindPosted + bigBlindPosted);
    }

    public void StartHand(GameState gameState, short dealerPosition = 0)
    {
        if (!CanStartHand)
            throw new InvalidOperationException("Table cannot start a hand right now.");
        if (dealerPosition < 0 || dealerPosition >= MAX_PLAYERS)
            throw new ArgumentOutOfRangeException(nameof(dealerPosition), "Dealer seat is out of range.");
        if (!Players.Any(p => p.Seat == dealerPosition))
            throw new InvalidOperationException($"Dealer seat {dealerPosition} is not occupied.");

        CurrentGameState = gameState ?? throw new ArgumentNullException(nameof(gameState));

        foreach (Player player in Players)
        {
            player.Reset();
        }

        CurrentGameState.StartHand(dealerPosition, BIG_BLIND);
        IsHandRunning = true;
    }

    public void EndHand()
    {
        IsHandRunning = false;
        CurrentGameState = null;
    }
    
    private Player GetPlayerBySeat(short seat)
    {
        Player? player = Players.FirstOrDefault(p => p.Seat == seat);
        if (player == null)
            throw new InvalidOperationException($"No player found in seat {seat}.");
    
        return player;
    }
    
    private short GetNextOccupiedSeat(short fromSeat)
    {
        for (short step = 1; step <= MAX_PLAYERS; step++)
        {
            short candidate = (short)((fromSeat + step) % MAX_PLAYERS);
            if (Players.Any(p => p.Seat == candidate))
                return candidate;
        }
    
        throw new InvalidOperationException("No occupied seat found.");
    }
}