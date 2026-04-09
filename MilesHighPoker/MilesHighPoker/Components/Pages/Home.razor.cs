using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Components.Pages;

public partial class Home
{
    private static readonly String CardBackPath = "/images/cards/card_back.png";
    private static readonly Card?[] TestingCards =
    [
        new() { Rank = CardRank.Ace, Suit = CardSuit.Spades },
        new() { Rank = CardRank.King, Suit = CardSuit.Hearts },
        new() { Rank = CardRank.Queen, Suit = CardSuit.Diamonds },
        new() { Rank = CardRank.Jack, Suit = CardSuit.Clubs },
        new() { Rank = CardRank.Ten, Suit = CardSuit.Spades }
    ];
    private Card?[] CommunityCards { get; set; } = new Card[5];
    private uint Pot { get; set; } = 0;

    private static String ToCardFile(Card card)
    {
        String rank = card.Rank switch
        {
            CardRank.Two => "2",
            CardRank.Three => "3",
            CardRank.Four => "4",
            CardRank.Five => "5",
            CardRank.Six => "6",
            CardRank.Seven => "7",
            CardRank.Eight => "8",
            CardRank.Nine => "9",
            CardRank.Ten => "10",
            CardRank.Jack => "jack",
            CardRank.Queen => "queen",
            CardRank.King => "king",
            CardRank.Ace => "ace",
            _ => throw new ArgumentOutOfRangeException()
        };

        String suit = card.Suit switch
        {
            CardSuit.Clubs => "clubs",
            CardSuit.Diamonds => "diamonds",
            CardSuit.Hearts => "hearts",
            CardSuit.Spades => "spades",
            _ => throw new ArgumentOutOfRangeException()
        };

        return $"images/cards/{rank}_of_{suit}.png";
    }
}