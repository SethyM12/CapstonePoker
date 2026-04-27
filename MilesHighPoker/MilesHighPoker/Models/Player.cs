using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Models;

public sealed class Player
{
    private readonly List<Card> cards = [];

    public String Name { get; private set; }
    public uint Id { get; }
    public String ConnectionId { get; private set; }
    public IReadOnlyList<Card> Cards => cards;

    public uint Chips { get; private set; }
    public short Seat { get; private set; }

    public bool Folded { get; private set; }
    public uint Bet { get; private set; }

    public bool IsAllIn => Chips == 0 && !Folded;
    public bool CanAct => !Folded && Chips > 0;

    public Player(String name, uint id, String connectionId, short seat, uint startingChips)
    {
        if (String.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Player name is required.", nameof(name));
        if (String.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("ConnectionId is required.", nameof(connectionId));
        if (startingChips == 0)
            throw new ArgumentException("Starting chips must be greater than zero.", nameof(startingChips));

        Name = name;
        Id = id;
        ConnectionId = connectionId;
        Seat = seat;
        Chips = startingChips;
    }

    public void UpdateConnection(String connectionId)
    {
        if (String.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("ConnectionId is required.", nameof(connectionId));

        ConnectionId = connectionId;
    }

    public void ReceiveDealtCard(Card card)
    {
        if (card == null)
            throw new ArgumentNullException(nameof(card));

        if (cards.Count >= 2)
            throw new InvalidOperationException("Player already has two hole cards.");

        cards.Add(card);
    }

    // Amount is the incremental chips to commit right now (not total street bet).
    public uint PlaceBet(uint amount)
    {
        if (Folded)
            throw new InvalidOperationException("Folded player cannot bet.");
        if (amount == 0)
            throw new InvalidOperationException("Bet amount must be greater than zero.");
        if (amount > Chips)
            throw new InvalidOperationException("Cannot bet more chips than available.");

        checked
        {
            Chips -= amount;
            Bet += amount;
        }

        return amount;
    }

    // Blinds can be short (all-in) if player has fewer chips than required blind.
    public uint PostBlind(uint amount)
    {
        if (Folded)
            throw new InvalidOperationException("Folded player cannot post blind.");
        if (amount == 0)
            throw new InvalidOperationException("Blind amount must be greater than zero.");

        uint commit = Math.Min(amount, Chips);
        if (commit == 0)
            return 0;

        checked
        {
            Chips -= commit;
            Bet += commit;
        }

        return commit;
    }

    // Match target total bet for this street, or go all-in if short.
    public uint CallTo(uint targetBet)
    {
        if (Folded)
            throw new InvalidOperationException("Folded player cannot call.");

        if (targetBet <= Bet)
            return 0;

        uint needed = targetBet - Bet;
        uint commit = Math.Min(needed, Chips);

        if (commit == 0)
            return 0;

        checked
        {
            Chips -= commit;
            Bet += commit;
        }

        return commit;
    }

    public void WinPot(uint amount)
    {
        checked
        {
            Chips += amount;
        }
    }

    public void Fold()
    {
        Folded = true;
    }

    public void ResetBetForNewStreet()
    {
        Bet = 0;
    }

    public void Reset()
    {
        Folded = false;
        Bet = 0;
        cards.Clear();
    }
}
