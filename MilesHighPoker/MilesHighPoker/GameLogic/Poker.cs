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
    
    public HandStreet CurrentStreet { get; private set; } = HandStreet.PreDeal;

    public uint CurrentBet { get; set; } = 0;

    public PokerGame(Deck deck)
    {
        Deck = deck;
        Deck.Shuffle();
    }
    
    public Card[] GetCommunityCards() => (Card[])CommunityCards.Clone();
    
    public void Deal(List<Player> players)
    {
        if(CurrentStreet != HandStreet.PreDeal)
            throw new InvalidOperationException("Cards can only be dealt at the start of a round.");
        
        if (players == null || players.Count == 0)
            throw new ArgumentException("At least one player is required.", nameof(players));
        
        for (int i = 0; i < 2; i++)
        {
            foreach (Player p in players)
            {
                p.ReceiveDealtCard(Deck.Draw());
            }
        }

        CurrentStreet = HandStreet.PreFlop;
    }

    public void Flop()
    {
        if (CurrentStreet != HandStreet.PreFlop)
            throw new InvalidOperationException("Flop can only be dealt after pre-flop.");
        
        for(int i = 0; i < FLOP_CARD_COUNT; i++)
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
        if (CurrentStreet != HandStreet.River && CurrentStreet != HandStreet.Showdown)
            throw new InvalidOperationException("Hand type can only be evaluated after river.");
        
        Card[] allCards = playerCards.Concat(CommunityCards).ToArray();
        
        int[] rankGroups = allCards
            .GroupBy(c => c.Rank)
            .Select(g => g.Count())
            .OrderByDescending(c => c)
            .ToArray();
        
        CurrentStreet = HandStreet.Showdown;
        
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
            .Select(c => (int)c.Rank)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        // Ace can be 1 in straight
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