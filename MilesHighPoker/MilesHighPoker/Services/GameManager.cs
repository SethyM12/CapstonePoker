using System.Collections.Concurrent;
using MilesHighPoker.GameLogic;
using MilesHighPoker.Models;

namespace MilesHighPoker.Services;

public sealed class GameManager
{
    private readonly TableRegistry tableRegistry;
    private readonly ConcurrentDictionary<String, TurnEngine> activeEngines = new();
    private readonly ConcurrentDictionary<String, object> tableJoinLocks = new();
    private readonly ConcurrentDictionary<String, short> lastDealerSeatByTable = new();

    public GameManager(TableRegistry tableRegistry)
    {
        this.tableRegistry = tableRegistry ?? throw new ArgumentNullException(nameof(tableRegistry));
    }

    public Table GetOrCreateTable(String tableId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));

        return tableRegistry.GetOrCreateTable(tableId);
    }
    
    public String CreateNewTableId()
    {
        String tableId = Guid.NewGuid().ToString("N");
        tableRegistry.GetOrCreateTable(tableId);
        return tableId;
    }

    public bool TryJoinGame(String tableId, String name, uint playerId, String connectionId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));
        if (String.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Player name is required.", nameof(name));
        if (String.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));

        Table table = GetOrCreateTable(tableId);
        object joinLock = tableJoinLocks.GetOrAdd(tableId, _ => new object());

        lock (joinLock)
        {
            if (table.IsHandRunning)
                return false;

            if (table.Players.Any(p => String.Equals(p.ConnectionId, connectionId, StringComparison.Ordinal)))
                return true;

            if (!table.CanJoinTable)
                return false;

            short seat = (short)GetNextAvailableSeat(table);
            Player player = new Player(name, playerId, connectionId, seat, Table.STARTING_MONEY);

            bool added = table.TryAddPlayer(player);

            Console.WriteLine($"Join result table={tableId} name={name} conn={connectionId} seat={seat} added={added} playersAfter={table.Players.Count}");

            return added;
        }
    }

    public bool TryLeaveGame(String tableId, String connectionId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));
        if (String.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));

        if (!tableRegistry.TryGetTable(tableId, out Table? table) || table == null)
            return false;

        bool removed = table.RemovePlayer(connectionId);
        if (!removed)
            return false;

        if (table.Players.Count == 0)
        {
            tableRegistry.RemoveTable(tableId);
            activeEngines.TryRemove(tableId, out _);
            tableJoinLocks.TryRemove(tableId, out _);
            lastDealerSeatByTable.TryRemove(tableId, out _);
        }

        return true;
    }

    public bool TryStartHand(String tableId, short dealerPosition = -1)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));

        Table table = GetTableOrThrow(tableId);

        if (table.IsHandRunning)
            return false;

        if (!table.CanStartHand)
            return false;

        // Respect explicit dealerPosition if passed (>= 0), otherwise resolve using
        // the existing rotation/random logic.
        short resolvedDealerSeat;
        if (dealerPosition >= 0)
        {
            // Validate the requested seat is occupied and active
            if (!table.Players.Any(p => p.Seat == dealerPosition && p.Chips > 0))
                return false; // invalid dealer request

            resolvedDealerSeat = dealerPosition;
            // record it as last dealer so the next hand rotates from here
            lastDealerSeatByTable[tableId] = resolvedDealerSeat;
        }
        else
        {
            resolvedDealerSeat = ResolveDealerSeatForNextHand(tableId, table);
        }
        
        table.DealerSeat = resolvedDealerSeat;
        GameState gameState = new GameState();
        table.StartHand(gameState, resolvedDealerSeat);

        TurnEngine turnEngine = new TurnEngine(table, gameState);
        activeEngines[tableId] = turnEngine;

        turnEngine.BeginStreet(table.CurrentGameState!.CurrentPlayerPosition);
        return true;
    }

    public TurnStepResult ProcessAction(String tableId, short actingSeat, PlayerAction action, uint? totalBet = null)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));

        Table table = GetTableOrThrow(tableId);

        if (!activeEngines.TryGetValue(tableId, out TurnEngine? turnEngine))
            throw new InvalidOperationException("No active hand exists for this table.");

        TurnStepResult result = turnEngine.ApplyAction(actingSeat, action, totalBet);

        if (result == TurnStepResult.BettingRoundComplete)
        {
            table.CurrentGameState!.SetAwaitingDealerAdvance(true);
        }
        else if (result == TurnStepResult.HandComplete)
        {
            ResolveFoldWin(table);
            EndHand(tableId);
        }

        return result;
    }

    public bool TryGetTable(String tableId, out Table? table)
    {
        if (String.IsNullOrWhiteSpace(tableId))
        {
            table = null;
            return false;
        }

        return tableRegistry.TryGetTable(tableId, out table);
    }

    private short DecideFirstDealer(Table table)
    {
        if (table == null)
            throw new ArgumentNullException(nameof(table));

        List<short> eligibleSeats = table.Players
            .Where(player => player.Chips > 0)
            .Select(player => player.Seat)
            .ToList();

        if (eligibleSeats.Count < 2)
            throw new InvalidOperationException("At least 2 active players are required to choose a dealer.");

        int randomIndex = Random.Shared.Next(eligibleSeats.Count);
        return eligibleSeats[randomIndex];
    }
    
    public bool TryAdvanceStreet(String tableId, short requestingSeat)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));

        Table table = GetTableOrThrow(tableId);
        GameState gameState = table.CurrentGameState
            ?? throw new InvalidOperationException("No game state available.");

        // Validate dealer
        if (requestingSeat != gameState.DealerPosition)
            throw new InvalidOperationException("Only the dealer can advance the street.");

        // Validate state
        if (!gameState.AwaitingDealerAdvance)
            throw new InvalidOperationException("Hand is not awaiting dealer advance.");

        if (!activeEngines.TryGetValue(tableId, out TurnEngine? turnEngine))
            throw new InvalidOperationException("No active hand exists for this table.");

        // Clear the flag
        gameState.SetAwaitingDealerAdvance(false);

        // Advance one step only
        AdvanceStreetOneStep(table, turnEngine);

        return true;
    }

    private void AdvanceStreetOneStep(Table table, TurnEngine turnEngine)
    {
        GameState gameState = table.CurrentGameState
            ?? throw new InvalidOperationException("No game state is available.");

        switch (gameState.CurrentStreet)
        {
            case HandStreet.PreFlop:
                table.RevealFlop();
                break;

            case HandStreet.Flop:
                table.RevealTurn();
                break;

            case HandStreet.Turn:
                table.RevealRiver();
                break;

            case HandStreet.River:
                table.ResolveShowdownAndPayout();
                EndHand(table.TableId);
                return;

            default:
                throw new InvalidOperationException("Street progression not allowed from current state.");
        }

        // After reveal, check if we can start betting
        if (CountPlayersWhoCanAct(table) >= 2)
        {
            short firstToActSeat = GetFirstCanActSeatAfter(table, gameState.DealerPosition);
            turnEngine.BeginStreet(firstToActSeat);
        }
        else
        {
            // Not enough players to bet; mark as waiting for dealer to continue
            gameState.SetAwaitingDealerAdvance(true);
        }
    }
    
    private int CountPlayersWhoCanAct(Table table)
    {
        return table.Players.Count(p => !p.Folded && p.CanAct);
    }

    private void ResolveFoldWin(Table table)
    {
        GameState gameState = table.CurrentGameState
            ?? throw new InvalidOperationException("No game state is available.");

        List<Player> contenders = table.Players
            .Where(p => !p.Folded)
            .ToList();

        if (contenders.Count != 1)
            throw new InvalidOperationException("Fold win resolution requires exactly one remaining player.");

        contenders[0].WinPot(gameState.Pot);
        table.EndHand();
    }

    private void EndHand(String tableId)
    {
        activeEngines.TryRemove(tableId, out _);
        
        if (tableRegistry.TryGetTable(tableId, out Table? table) && table != null)
        {
            table.DealerSeat = PeekNextDealerSeat(tableId, table);
        }
    }

    private Table GetTableOrThrow(String tableId)
    {
        if (!tableRegistry.TryGetTable(tableId, out Table? table) || table == null)
            throw new InvalidOperationException($"Table '{tableId}' was not found.");

        return table;
    }

    private int GetNextAvailableSeat(Table table)
    {
        for (int i = 0; i < Table.MAX_PLAYERS; i++)
        {
            if (table.Players.All(p => p.Seat != i))
                return i;
        }

        throw new InvalidOperationException("No open seat is available.");
    }

    private short GetFirstCanActSeatAfter(Table table, short fromSeat)
    {
        for (int i = 1; i <= Table.MAX_PLAYERS; i++)
        {
            short candidate = (short)((fromSeat + i) % Table.MAX_PLAYERS);

            if (table.Players.Any(p => p.Seat == candidate && !p.Folded && p.CanAct))
                return candidate;
        }

        throw new InvalidOperationException("No eligible player can act.");
    }
    
    private short ResolveDealerSeatForNextHand(String tableId, Table table)
    {
        if (!lastDealerSeatByTable.TryGetValue(tableId, out short previousDealerSeat))
        {
            short firstDealerSeat = DecideFirstDealer(table);
            lastDealerSeatByTable[tableId] = firstDealerSeat;
            return firstDealerSeat;
        }
    
        short nextDealerSeat = GetNextOccupiedSeatClockwise(table, previousDealerSeat);
        lastDealerSeatByTable[tableId] = nextDealerSeat;
        return nextDealerSeat;
    }
    private static short GetNextOccupiedSeatClockwise(Table table, short fromSeat)
    {
        for (int i = 1; i <= Table.MAX_PLAYERS; i++)
        {
            short candidate = (short)((fromSeat + i) % Table.MAX_PLAYERS);
    
            if (table.Players.Any(player => player.Seat == candidate && player.Chips > 0))
                return candidate;
        }
    
        throw new InvalidOperationException("No occupied seat with chips found.");
    }
    private short PeekNextDealerSeat(String tableId, Table table)
    {
        // If we don't have a last dealer recorded, choose a first dealer randomly (don't mutate)
        if (!lastDealerSeatByTable.TryGetValue(tableId, out short previousDealer))
        {
            return DecideFirstDealer(table);
        }
    
        // Next occupied seat clockwise from the previous dealer
        return GetNextOccupiedSeatClockwise(table, previousDealer);
    }
}