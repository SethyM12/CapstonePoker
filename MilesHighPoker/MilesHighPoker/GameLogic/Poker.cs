using MilesHighPoker.Models;

namespace MilesHighPoker.GameLogic;

public enum PokerHands
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
    Two,
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
    public List<Card> Cards { get; set; } = [];

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
        
        Card card = Cards[0];
        Cards.RemoveAt(0);
        return card;
    }
}

public class Poker
{
    private readonly short FLOP_CARD_COUNT = 3;
    private Deck Deck { get; init; }
    private Card[] CommunityCards { get; init; } = new Card[5];

    public Poker(Deck deck)
    {
        Deck = deck;
        Deck.Shuffle();
    }
    
    public void Deal(List<Player> players)
    {
        if (players == null || players.Count == 0)
            throw new ArgumentException("At least one player is required.", nameof(players));
        
        for (int round = 0; round < 2; round++)
        {
            for (int seat = 0; seat < players.Count; seat++)
            {
                players[seat].Cards.Add(Deck.Draw());
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

    public Player ScoreHands(List<Card> player1Cards, List<Card> player2Cards)
    {
        return null;
    }
}