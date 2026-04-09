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

public record Card
{
    public CardRank Rank { get; init; }
    public CardSuit Suit { get; init; }
}

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
        Random rand = new Random();
        int n = Cards.Count;
        while (n > 1)
        {
            n--;
            int k = rand.Next(n + 1);
            Card temp = Cards[k];
            Cards[k] = Cards[n];
            Cards[n] = temp;
        }
    }

    public Card Draw()
    {
        if (Cards.Count == 0)
            throw new InvalidOperationException("Deck is empty");
        
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

    public PokerGame(Deck deck)
    {
        Deck = deck;
        Deck.Shuffle();
    }
    
    public void Deal(List<Player> players)
    {
        if (players == null || players.Count == 0)
            throw new ArgumentException("At least one player is required.", nameof(players));
        
        for (int i = 0; i < 2; i++)
        {
            foreach (Player p in players)
            {
                p.Cards.Add(Deck.Draw());
            }
        }
    }

    public void Flop()
    {
        for(int i = 0; i < FLOP_CARD_COUNT; i++)
        {
            CommunityCards[i] = Deck.Draw();
        }
    }

    public void Turn()
    {
        CommunityCards[3] = Deck.Draw();
    }

    public void River()
    {
        CommunityCards[4] = Deck.Draw();
    }
    
    public PokerHand GetHandType(Card[] playerCards)
    {        
        Card[] allCards = playerCards.Concat(CommunityCards).ToArray();
        
        int[] rankGroups = allCards
            .GroupBy(c => c.Rank)
            .Select(g => g.Count())
            .OrderByDescending(c => c)
            .ToArray();
        
        if (ContainsStraightFlush(allCards)) return PokerHand.StraightFlush;
        if (rankGroups[0] == 4) return PokerHand.FourOfAKind;
        if (rankGroups.Contains(3) && rankGroups.Count(c => c >= 2) >= 2) return PokerHand.FullHouse;
        if(ContainsFlush(allCards)) return PokerHand.Flush;
        if(ContainsStraight(allCards)) return PokerHand.Straight;
        if (rankGroups[0] == 3) return PokerHand.ThreeOfAKind;
        if (rankGroups.Count(c => c == 2) >= 2) return PokerHand.TwoPair;
        if (rankGroups.Count(c => c == 2) == 1) return PokerHand.Pair;
        return  PokerHand.HighCard;
    }

    private static bool ContainsStraightFlush(Card[] cards)
    {
        return cards.GroupBy(c => c.Suit)
            .Any(g => g.Count() >= 5 && ContainsStraight(g.ToArray()));
    }

    private static bool ContainsFlush(Card[] playerCards)
    {
        return playerCards
            .GroupBy(c => c.Suit)
            .Any(g => g.Count() >= 5);
    }

    private static bool ContainsStraight(Card[] playerCards)
    {
        List<int> ranks = playerCards
            .Select(c => (int)c.Rank + 2)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        // Ace can be low in straight
        if (ranks.Contains(14))
        {
            ranks.Insert(0, 1);
        }

        int runLength = 1;
        
        for (int i = 1; i < ranks.Count; i++)
        {
            if (ranks[i] == ranks[i - 1] + 1)
            {
                runLength++;
                if (runLength >= 5)
                    return true;
            }
            else
            {
                runLength = 1;
            }
        }

        return false;
    }
}