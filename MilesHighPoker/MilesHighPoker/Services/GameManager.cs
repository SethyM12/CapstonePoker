using System.Collections.Concurrent;
using MilesHighPoker.GameLogic;
using MilesHighPoker.Models;

namespace MilesHighPoker.Services;

public sealed class GameManager
{
    private readonly TableRegistry tableRegistry;
    private readonly ConcurrentDictionary<String, TurnEngine> activeEngines = new();

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

    public bool TryJoinGame(String tableId, String name, uint playerId, String connectionId)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));
        if (String.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Player name is required.", nameof(name));
        if (String.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));

        Table table = GetOrCreateTable(tableId);

        if (table.IsHandRunning)
            return false;

        if (!table.CanJoinTable)
            return false;

        if (table.Players.Any(p => String.Equals(p.ConnectionId, connectionId, StringComparison.Ordinal)))
            return true;

        short seat = (short)GetNextAvailableSeat(table);
        Player player = new Player(name, playerId, connectionId, seat, Table.STARTING_MONEY);

        return table.TryAddPlayer(player);
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
        }

        return true;
    }

    public bool TryStartHand(String tableId, short dealerPosition = 0)
    {
        if (String.IsNullOrWhiteSpace(tableId))
            throw new ArgumentException("Table id is required.", nameof(tableId));

        Table table = GetTableOrThrow(tableId);

        if (table.IsHandRunning)
            return false;

        if (!table.CanStartHand)
            return false;

        if (!table.Players.Any(p => p.Seat == dealerPosition && p.Chips > 0))
            throw new InvalidOperationException($"Dealer seat {dealerPosition} is not occupied by an active player.");

        GameState gameState = new GameState();
        table.StartHand(gameState, dealerPosition);

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
            AdvanceStreetOrResolveShowdown(table, turnEngine);
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

    private void AdvanceStreetOrResolveShowdown(Table table, TurnEngine turnEngine)
    {
        GameState gameState = table.CurrentGameState
            ?? throw new InvalidOperationException("No game state is available.");

        short firstToActSeat = GetFirstCanActSeatAfter(table, gameState.DealerPosition);

        switch (gameState.CurrentStreet)
        {
            case HandStreet.PreFlop:
                table.RevealFlop();
                turnEngine.BeginStreet(firstToActSeat);
                break;

            case HandStreet.Flop:
                table.RevealTurn();
                turnEngine.BeginStreet(firstToActSeat);
                break;

            case HandStreet.Turn:
                table.RevealRiver();
                turnEngine.BeginStreet(firstToActSeat);
                break;

            case HandStreet.River:
                table.ResolveShowdownAndPayout();
                EndHand(table.TableId);
                break;

            default:
                throw new InvalidOperationException("Street progression is not allowed from the current state.");
        }
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

            if (table.Players.Any(p => p.Seat == candidate && p.CanAct))
                return candidate;
        }

        throw new InvalidOperationException("No eligible player can act.");
    }
}
