using MilesHighPoker.Models;

namespace MilesHighPoker.GameLogic;

public enum PokerHand
{
    HighCard,
    Pair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush
}

public enum CardRank
{
    Two = 2,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King,
    Ace
}

public enum CardSuit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum HandStreet
{
    PreDeal,
    PreFlop,
    Flop,
    Turn,
    River,
    Showdown
}

public enum PlayerAction
{
    Fold,
    Check,
    Call,
    Bet,
    Raise
}

public record Card
{
    public CardRank Rank { get; init; }
    public CardSuit Suit { get; init; }
}

// Record combines hand type and card values to provide correct
// scoring when 2 players have the same hand type
public sealed record HandScore(PokerHand Hand, int[] RankedValues);

public class Deck
{
    private List<Card> Cards { get; set; } = [];

    public Deck()
    {
        foreach (CardSuit suit in Enum.GetValues(typeof(CardSuit)))
        {
            foreach (CardRank rank in Enum.GetValues(typeof(CardRank)))
            {
                Cards.Add(new Card { Rank = rank, Suit = suit });
            }
        }
    }

    public void Shuffle()
    {
        int n = Cards.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Shared.Next(n + 1);
            Card temp = Cards[k];
            Cards[k] = Cards[n];
            Cards[n] = temp;
        }
    }

    public Card Draw()
    {
        if (Cards.Count == 0)
            throw new InvalidOperationException("Deck is empty.");

        Card card = Cards[Cards.Count - 1];
        Cards.RemoveAt(Cards.Count - 1);
        return card;
    }
}

public class PokerGame
{
    private static readonly short FLOP_CARD_COUNT = 3;

    private Deck Deck { get; init; }
    private Card[] CommunityCards { get; init; } = new Card[5];

    public HandStreet CurrentStreet { get; private set; } = HandStreet.PreDeal;
    
    public uint CurrentBet { get; set; } = 0;

    public PokerGame(Deck deck)
    {
        Deck = deck ?? throw new ArgumentNullException(nameof(deck));
        Deck.Shuffle();
    }

    public Card[] GetCommunityCards()
    {
        return (Card[])CommunityCards.Clone();
    }

    public void Deal(List<Player> players)
    {
        if (CurrentStreet != HandStreet.PreDeal)
            throw new InvalidOperationException("Cards can only be dealt at the start of a hand.");

        if (players == null)
            throw new ArgumentNullException(nameof(players));
        if (players.Count < 2)
            throw new ArgumentException("At least two players are required to deal a hand.", nameof(players));

        // Two rounds around the table.
        for (int round = 0; round < 2; round++)
        {
            foreach (Player player in players)
            {
                player.ReceiveDealtCard(Deck.Draw());
            }
        }

        CurrentStreet = HandStreet.PreFlop;
    }

    public void Flop()
    {
        if (CurrentStreet != HandStreet.PreFlop)
            throw new InvalidOperationException("Flop can only be dealt after pre-flop.");

        for (int i = 0; i < FLOP_CARD_COUNT; i++)
        {
            CommunityCards[i] = Deck.Draw();
        }

        CurrentStreet = HandStreet.Flop;
    }

    public void Turn()
    {
        if (CurrentStreet != HandStreet.Flop)
            throw new InvalidOperationException("Turn can only be dealt after flop.");

        CommunityCards[3] = Deck.Draw();
        CurrentStreet = HandStreet.Turn;
    }

    public void River()
    {
        if (CurrentStreet != HandStreet.Turn)
            throw new InvalidOperationException("River can only be dealt after turn.");

        CommunityCards[4] = Deck.Draw();
        CurrentStreet = HandStreet.River;
    }

    public PokerHand GetHandType(Card[] playerCards)
    {
        return GetHandScore(playerCards).Hand;
    }

    public HandScore GetHandScore(Card[] playerCards)
    {
        if (CurrentStreet != HandStreet.River && CurrentStreet != HandStreet.Showdown)
            throw new InvalidOperationException("Hand scoring can only be evaluated after river.");

        if (playerCards == null)
            throw new ArgumentNullException(nameof(playerCards));

        Card[] allCards = playerCards.Concat(CommunityCards).ToArray();

        if (allCards.Any(c => c == null))
            throw new InvalidOperationException("Cannot evaluate hand with incomplete cards.");

        HandScore result = EvaluateHandScore(allCards);

        // Keep existing behavior so GameState.IsHandOver works with current design.
        CurrentStreet = HandStreet.Showdown;
        return result;
    }

    public static int CompareHandScores(HandScore left, HandScore right)
    {
        if (left == null) throw new ArgumentNullException(nameof(left));
        if (right == null) throw new ArgumentNullException(nameof(right));

        int handCompare = left.Hand.CompareTo(right.Hand);
        if (handCompare != 0)
            return handCompare;

        int max = Math.Max(left.RankedValues.Length, right.RankedValues.Length);
        for (int i = 0; i < max; i++)
        {
            int lv = i < left.RankedValues.Length ? left.RankedValues[i] : 0;
            int rv = i < right.RankedValues.Length ? right.RankedValues[i] : 0;
            if (lv != rv)
                return lv.CompareTo(rv);
        }

        return 0;
    }

    private static HandScore EvaluateHandScore(Card[] cards)
    {
        // Straight Flush
        int straightFlushHigh = GetStraightFlushHigh(cards);
        if (straightFlushHigh > 0)
            return new HandScore(PokerHand.StraightFlush, [straightFlushHigh]);

        Dictionary<int, int> rankCounts = cards
            .GroupBy(c => (int)c.Rank)
            .ToDictionary(g => g.Key, g => g.Count());

        // Four of a Kind
        int fourRank = rankCounts
            .Where(kv => kv.Value == 4)
            .Select(kv => kv.Key)
            .DefaultIfEmpty(0)
            .Max();

        if (fourRank > 0)
        {
            int kicker = cards
                .Select(c => (int)c.Rank)
                .Where(r => r != fourRank)
                .DefaultIfEmpty(0)
                .Max();

            return new HandScore(PokerHand.FourOfAKind, [fourRank, kicker]);
        }

        // Full House
        List<int> trips = rankCounts
            .Where(kv => kv.Value >= 3)
            .Select(kv => kv.Key)
            .OrderByDescending(r => r)
            .ToList();

        if (trips.Count > 0)
        {
            int bestTrip = trips[0];

            int bestPair = rankCounts
                .Where(kv => kv.Key != bestTrip && kv.Value >= 2)
                .Select(kv => kv.Key)
                .DefaultIfEmpty(0)
                .Max();

            // Two trips can form full house: highest trip + second trip as pair
            if (bestPair == 0 && trips.Count > 1)
                bestPair = trips[1];

            if (bestPair > 0)
                return new HandScore(PokerHand.FullHouse, [bestTrip, bestPair]);
        }

        // Flush
        int[] bestFlushRanks = GetBestFlushRanks(cards);
        if (bestFlushRanks.Length == 5)
            return new HandScore(PokerHand.Flush, bestFlushRanks);

        // Straight
        int straightHigh = GetStraightHigh(cards.Select(c => (int)c.Rank));
        if (straightHigh > 0)
            return new HandScore(PokerHand.Straight, [straightHigh]);

        // Three of a Kind
        int threeRank = rankCounts
            .Where(kv => kv.Value == 3)
            .Select(kv => kv.Key)
            .DefaultIfEmpty(0)
            .Max();

        if (threeRank > 0)
        {
            int[] kickers = cards
                .Select(c => (int)c.Rank)
                .Where(r => r != threeRank)
                .OrderByDescending(r => r)
                .Take(2)
                .ToArray();

            return new HandScore(PokerHand.ThreeOfAKind, [threeRank, .. kickers]);
        }

        // Two Pair
        List<int> pairRanks = rankCounts
            .Where(kv => kv.Value >= 2)
            .Select(kv => kv.Key)
            .OrderByDescending(r => r)
            .ToList();

        if (pairRanks.Count >= 2)
        {
            int highPair = pairRanks[0];
            int lowPair = pairRanks[1];

            int kicker = cards
                .Select(c => (int)c.Rank)
                .Where(r => r != highPair && r != lowPair)
                .DefaultIfEmpty(0)
                .Max();

            return new HandScore(PokerHand.TwoPair, [highPair, lowPair, kicker]);
        }

        // Pair
        if (pairRanks.Count == 1)
        {
            int pair = pairRanks[0];
            int[] kickers = cards
                .Select(c => (int)c.Rank)
                .Where(r => r != pair)
                .OrderByDescending(r => r)
                .Take(3)
                .ToArray();

            return new HandScore(PokerHand.Pair, [pair, .. kickers]);
        }

        // High Card
        int[] highCards = cards
            .Select(c => (int)c.Rank)
            .OrderByDescending(r => r)
            .Take(5)
            .ToArray();

        return new HandScore(PokerHand.HighCard, highCards);
    }

    private static int GetStraightFlushHigh(Card[] cards)
    {
        int best = 0;

        foreach (IGrouping<CardSuit, Card> suitGroup in cards.GroupBy(c => c.Suit))
        {
            if (suitGroup.Count() < 5)
                continue;

            int high = GetStraightHigh(suitGroup.Select(c => (int)c.Rank));
            if (high > best)
                best = high;
        }

        return best;
    }

    private static int[] GetBestFlushRanks(Card[] cards)
    {
        int[] best = [];

        foreach (IGrouping<CardSuit, Card> suitGroup in cards.GroupBy(c => c.Suit))
        {
            if (suitGroup.Count() < 5)
                continue;

            int[] candidate = suitGroup
                .Select(c => (int)c.Rank)
                .OrderByDescending(r => r)
                .Take(5)
                .ToArray();

            if (CompareRankVectors(candidate, best) > 0)
                best = candidate;
        }

        return best;
    }

    private static int GetStraightHigh(IEnumerable<int> ranksInput)
    {
        List<int> ranks = ranksInput
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        if (ranks.Count < 5)
            return 0;

        // Ace-low straight support: A-2-3-4-5
        if (ranks.Contains((int)CardRank.Ace))
            ranks.Insert(0, 1);

        int runLength = 1;
        int bestHigh = 0;

        for (int i = 1; i < ranks.Count; i++)
        {
            if (ranks[i] == ranks[i - 1] + 1)
            {
                runLength++;
                if (runLength >= 5)
                    bestHigh = ranks[i];
            }
            else
            {
                runLength = 1;
            }
        }

        return bestHigh;
    }

    private static int CompareRankVectors(int[] left, int[] right)
    {
        int max = Math.Max(left.Length, right.Length);
        for (int i = 0; i < max; i++)
        {
            int lv = i < left.Length ? left[i] : 0;
            int rv = i < right.Length ? right[i] : 0;
            if (lv != rv)
                return lv.CompareTo(rv);
        }

        return 0;
    }
}
