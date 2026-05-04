using MilesHighPoker.Models;

namespace MilesHighPoker.GameLogic;

public enum TurnStepResult
{
    AwaitingNextAction,
    BettingRoundComplete,
    HandComplete
}

public sealed class TurnEngine
{
    private readonly Table table;
    private readonly GameState gameState;
    private readonly HashSet<short> actedThisStreet = [];

    public TurnEngine(Table table, GameState gameState)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
    }

    public void BeginStreet(short firstToActSeat)
    {
        actedThisStreet.Clear();
        gameState.SetCurrentTurn(firstToActSeat);
    }

    // totalBet is the player's total bet on this street after action (used for Bet/Raise).
    public TurnStepResult ApplyAction(short actingSeat, PlayerAction action, uint? totalBet = null)
    {
        EnsureHandRunning();

        if (gameState.CurrentPlayerPosition != actingSeat)
            throw new InvalidOperationException($"It is not seat {actingSeat}'s turn.");

        Player actor = GetPlayerBySeat(actingSeat);

        if (!actor.CanAct && action != PlayerAction.Fold)
            throw new InvalidOperationException("Player cannot act.");

        switch (action)
        {
            case PlayerAction.Fold:
                actor.Fold();
                break;

            case PlayerAction.Check:
                if (actor.Bet != gameState.CurrentBet)
                    throw new InvalidOperationException("Cannot check when facing a bet.");
                break;

            case PlayerAction.Call:
            {
                uint committed = actor.CallTo(gameState.CurrentBet);
                gameState.AddToPot(committed);
                break;
            }

            case PlayerAction.Bet:
            case PlayerAction.Raise:
            {
                if (totalBet is null)
                    throw new ArgumentException("Bet/Raise requires totalBet.");

                if (totalBet.Value <= gameState.CurrentBet)
                    throw new InvalidOperationException("Bet/Raise must exceed current bet.");

                if (totalBet.Value <= actor.Bet)
                    throw new InvalidOperationException("totalBet must exceed player's current street bet.");

                uint toCommit = totalBet.Value - actor.Bet;
                uint committed = actor.PlaceBet(toCommit);
                gameState.AddToPot(committed);

                // Record aggressive action (updates CurrentBet / MinimumRaise / LastAggressorPosition).
                gameState.RecordAction(actor.Bet, actor.Seat);

                // New aggression resets "who has responded" tracking.
                actedThisStreet.Clear();
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action.");
        }

        actedThisStreet.Add(actor.Seat);

        if (CountUnfoldedPlayers() <= 1)
            return TurnStepResult.HandComplete;

        if (IsBettingRoundComplete())
            return TurnStepResult.BettingRoundComplete;

        gameState.SetCurrentTurn(GetNextActingSeat(actor.Seat));
        return TurnStepResult.AwaitingNextAction;
    }

    private bool IsBettingRoundComplete()
    {
        // Players who can still act this street.
        List<Player> actionable = table.Players
            .Where(p => !p.Folded && p.CanAct)
            .ToList();

        if (actionable.Count == 0)
            return true;

        bool everyoneMatched = actionable.All(p => p.Bet == gameState.CurrentBet);
        if (!everyoneMatched)
            return false;

        // No-bet round (checks): everyone must have acted once.
        // Bet/raise round: everyone must have responded since last aggression.
        return actionable.All(p => actedThisStreet.Contains(p.Seat));
    }

    private short GetNextActingSeat(short fromSeat)
    {
        for (int i = 1; i <= Table.MAX_PLAYERS; i++)
        {
            short candidate = (short)((fromSeat + i) % Table.MAX_PLAYERS);
            Player? player = table.Players.FirstOrDefault(p => p.Seat == candidate);

            if (player is { CanAct: true, Folded: false })
                return candidate;
        }

        throw new InvalidOperationException("No eligible player can act.");
    }

    private Player GetPlayerBySeat(short seat)
    {
        Player? player = table.Players.FirstOrDefault(p => p.Seat == seat);
        if (player == null)
            throw new InvalidOperationException($"No player in seat {seat}.");

        return player;
    }

    private int CountUnfoldedPlayers()
    {
        return table.Players.Count(p => !p.Folded);
    }

    private void EnsureHandRunning()
    {
        if (!table.IsHandRunning || table.CurrentGameState == null)
            throw new InvalidOperationException("No hand is running.");
    }
}
