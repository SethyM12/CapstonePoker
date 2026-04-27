using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Models;

public class Table
{
    private sealed record ScoredPlayer(Player Player, HandScore Score);

    public const short MAX_PLAYERS = 5;
    public const uint SMALL_BLIND = 5;
    public const uint BIG_BLIND = SMALL_BLIND * 2;
    public const uint MAX_BET = 150;
    public const uint STARTING_MONEY = 500;

    public String TableId { get; }

    public List<WaitingPlayer> WaitingPlayers { get; }
    public List<Player> Players { get; }
    public bool IsHandRunning { get; private set; }
    public GameState? CurrentGameState { get; private set; }

    public Table(String tableId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table ID cannot be null or empty.", nameof(tableId));

        TableId = tableId;
        WaitingPlayers = new List<WaitingPlayer>();
        Players = new List<Player>();
        IsHandRunning = false;
        CurrentGameState = null;
    }

    public bool CanJoinTable => Players.Count < MAX_PLAYERS;
    public bool CanStartHand => !IsHandRunning && Players.Count(p => p.Chips > 0) >= 2;

    public bool AddWaitingPlayer(WaitingPlayer waitingPlayer)
    {
        if (waitingPlayer == null) return false;
        if (String.IsNullOrWhiteSpace(waitingPlayer.Name)) return false;

        if (WaitingPlayers.Any(w => w.ConnectionId == waitingPlayer.ConnectionId)) return false;
        if (WaitingPlayers.Any(w => String.Equals(w.Name, waitingPlayer.Name, StringComparison.OrdinalIgnoreCase))) return false;
        if (Players.Any(p => String.Equals(p.Name, waitingPlayer.Name, StringComparison.OrdinalIgnoreCase))) return false;

        WaitingPlayers.Add(waitingPlayer);
        return true;
    }

    public bool RemoveWaitingPlayer(String connectionId)
    {
        int index = WaitingPlayers.FindIndex(w => w.ConnectionId == connectionId);
        if (index < 0) return false;

        WaitingPlayers.RemoveAt(index);
        return true;
    }

    public bool TryAddPlayer(Player player)
    {
        if (player == null) return false;
        if (!CanJoinTable) return false;

        if (player.Seat < 0 || player.Seat >= MAX_PLAYERS) return false;
        if (Players.Any(p => p.ConnectionId == player.ConnectionId)) return false;
        if (Players.Any(p => p.Seat == player.Seat)) return false;
        if (Players.Any(p => String.Equals(p.Name, player.Name, StringComparison.OrdinalIgnoreCase))) return false;

        Players.Add(player);
        return true;
    }

    public bool RemovePlayer(String connectionId)
    {
        int index = Players.FindIndex(p => p.ConnectionId == connectionId);
        if (index < 0) return false;

        Players.RemoveAt(index);
        return true;
    }

    public Player? GetPlayer(String connectionId)
    {
        return Players.FirstOrDefault(p => p.ConnectionId == connectionId);
    }

    public void StartHand(GameState gameState, short dealerPosition = 0)
    {
        if (!CanStartHand)
            throw new InvalidOperationException("Table cannot start a hand right now.");

        if (dealerPosition < 0 || dealerPosition >= MAX_PLAYERS)
            throw new ArgumentOutOfRangeException(nameof(dealerPosition), "Dealer seat is out of range.");

        if (!Players.Any(p => p.Seat == dealerPosition && p.Chips > 0))
            throw new InvalidOperationException($"Dealer seat {dealerPosition} is not occupied by an active player.");

        CurrentGameState = gameState ?? throw new ArgumentNullException(nameof(gameState));

        foreach (Player player in Players)
        {
            player.Reset();
        }

        CurrentGameState.StartHand(dealerPosition, BIG_BLIND);

        IsHandRunning = true;
        try
        {
            PostBlinds();

            List<Player> dealOrder = GetPlayersInDealOrder(dealerPosition);
            CurrentGameState.DealHoleCards(dealOrder);

            short preFlopFirstToActSeat = GetPreFlopFirstToActSeat(dealerPosition);
            CurrentGameState.SetCurrentTurn(preFlopFirstToActSeat);
        }
        catch
        {
            IsHandRunning = false;
            CurrentGameState = null;
            throw;
        }
    }

    public void RevealFlop()
    {
        EnsureRunningHand();
        CurrentGameState!.RevealFlop(GetActivePlayersForStreet());
        CurrentGameState.SetCurrentTurn(GetPostFlopFirstToActSeat());
    }

    public void RevealTurn()
    {
        EnsureRunningHand();
        CurrentGameState!.RevealTurn(GetActivePlayersForStreet());
        CurrentGameState.SetCurrentTurn(GetPostFlopFirstToActSeat());
    }

    public void RevealRiver()
    {
        EnsureRunningHand();
        CurrentGameState!.RevealRiver(GetActivePlayersForStreet());
        CurrentGameState.SetCurrentTurn(GetPostFlopFirstToActSeat());
    }

    public void EndHand()
    {
        IsHandRunning = false;
        CurrentGameState = null;
    }

    public void PostBlinds()
    {
        EnsureRunningHand();

        short dealerSeat = CurrentGameState!.DealerPosition;
        short smallBlindSeat = GetNextOccupiedSeat(dealerSeat);
        short bigBlindSeat = GetNextOccupiedSeat(smallBlindSeat);

        Player smallBlindPlayer = GetPlayerBySeat(smallBlindSeat);
        Player bigBlindPlayer = GetPlayerBySeat(bigBlindSeat);

        uint smallBlindPosted = smallBlindPlayer.PostBlind(SMALL_BLIND);
        uint bigBlindPosted = bigBlindPlayer.PostBlind(BIG_BLIND);

        CurrentGameState.AddToPot(smallBlindPosted + bigBlindPosted);

        CurrentGameState.ApplyPostedBlinds(bigBlindPosted, bigBlindSeat);
    }

    public IReadOnlyList<Player> ResolveShowdownAndPayout()
    {
        EnsureRunningHand();

        if (CurrentGameState!.CurrentStreet != HandStreet.River &&
            CurrentGameState.CurrentStreet != HandStreet.Showdown)
        {
            throw new InvalidOperationException("Showdown can only be resolved at river/showdown.");
        }

        List<Player> contenders = Players
            .Where(p => !p.Folded && p.Cards.Count == 2)
            .ToList();

        if (contenders.Count == 0)
            throw new InvalidOperationException("No eligible contenders at showdown.");

        if (contenders.Count == 1)
        {
            contenders[0].WinPot(CurrentGameState.Pot);
            EndHand();
            return contenders;
        }

        List<ScoredPlayer> scored = contenders
            .Select(p => new ScoredPlayer(p, CurrentGameState.GetHandScore(p.Cards.ToArray())))
            .ToList();

        ScoredPlayer best = scored[0];
        for (int i = 1; i < scored.Count; i++)
        {
            if (PokerGame.CompareHandScores(scored[i].Score, best.Score) > 0)
                best = scored[i];
        }

        List<Player> winners = scored
            .Where(s => PokerGame.CompareHandScores(s.Score, best.Score) == 0)
            .Select(s => s.Player)
            .ToList();

        uint pot = CurrentGameState.Pot;
        uint baseShare = pot / (uint)winners.Count;
        uint oddChip = pot % (uint)winners.Count;

        foreach (Player winner in winners)
        {
            if (baseShare > 0)
                winner.WinPot(baseShare);
        }

        if (oddChip > 0)
        {
            List<Player> oddChipOrder = GetClockwiseOrderFromDealer(winners, CurrentGameState.DealerPosition);
            oddChipOrder[0].WinPot(oddChip);
        }

        EndHand();
        return winners;
    }

    private void EnsureRunningHand()
    {
        if (!IsHandRunning || CurrentGameState == null)
            throw new InvalidOperationException("No hand is currently running.");
    }

    private List<Player> GetActivePlayersForStreet()
    {
        return Players.Where(p => p.Chips > 0 || p.Bet > 0).ToList();
    }

    private List<Player> GetPlayersInDealOrder(short dealerSeat)
    {
        return Players
            .Where(p => p.Chips > 0 || p.Bet > 0)
            .OrderBy(p =>
            {
                int distance = (p.Seat - dealerSeat + MAX_PLAYERS) % MAX_PLAYERS;
                return distance == 0 ? MAX_PLAYERS : distance;
            })
            .ToList();
    }

    private short GetPreFlopFirstToActSeat(short dealerSeat)
    {
        short smallBlindSeat = GetNextOccupiedSeat(dealerSeat);
        short bigBlindSeat = GetNextOccupiedSeat(smallBlindSeat);
        return GetNextOccupiedSeat(bigBlindSeat);
    }

    private short GetPostFlopFirstToActSeat()
    {
        return GetNextOccupiedSeat(CurrentGameState!.DealerPosition);
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
            if (Players.Any(p => p.Seat == candidate && p.Chips > 0))
                return candidate;
        }

        throw new InvalidOperationException("No occupied active seat found.");
    }

    private List<Player> GetClockwiseOrderFromDealer(List<Player> players, short dealerSeat)
    {
        return players
            .OrderBy(p =>
            {
                int distance = (p.Seat - dealerSeat + MAX_PLAYERS) % MAX_PLAYERS;
                return distance == 0 ? MAX_PLAYERS : distance;
            })
            .ToList();
    }
}
