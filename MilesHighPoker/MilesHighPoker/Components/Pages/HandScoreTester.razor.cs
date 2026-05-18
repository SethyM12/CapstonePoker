using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Components.Pages;

public partial class HandScoreTester : ComponentBase
{
    // bound selection arrays
    private CardRank[] CommunityRanks { get; set; } = new CardRank[5];
    private CardSuit[] CommunitySuits { get; set; } = new CardSuit[5];

    private CardRank[][] PlayerRanks { get; set; } = new CardRank[2][] { new CardRank[2], new CardRank[2] };
    private CardSuit[][] PlayerSuits { get; set; } = new CardSuit[2][] { new CardSuit[2], new CardSuit[2] };

    // computed Card objects (never null elements)
    private Card[] CommunityCards => BuildCommunityCards();
    private Card[][] PlayerCards => BuildPlayerCards();

    // evaluation results
    private HandScore? LeftHand;
    private HandScore? RightHand;
    private int Comparison = 0; // >0 left wins, <0 right wins, 0 tie
    private string ErrorMessage = string.Empty;

    protected override void OnInitialized()
    {
        ResetDefaults();
    }

    private void ResetDefaults()
    {
        ErrorMessage = string.Empty;
        LeftHand = null;
        RightHand = null;
        Comparison = 0;

        // sensible default board and player cards (no duplicates within these defaults)
        CommunityRanks = new CardRank[] { CardRank.Ace, CardRank.King, CardRank.Queen, CardRank.Jack, CardRank.Ten };
        CommunitySuits = new CardSuit[] { CardSuit.Hearts, CardSuit.Hearts, CardSuit.Hearts, CardSuit.Hearts, CardSuit.Hearts };

        PlayerRanks[0][0] = CardRank.Ace; PlayerSuits[0][0] = CardSuit.Clubs;
        PlayerRanks[0][1] = CardRank.Ace; PlayerSuits[0][1] = CardSuit.Diamonds;

        PlayerRanks[1][0] = CardRank.Two; PlayerSuits[1][0] = CardSuit.Clubs;
        PlayerRanks[1][1] = CardRank.Three; PlayerSuits[1][1] = CardSuit.Clubs;
    }

    private Card[] BuildCommunityCards()
    {
        var cards = new Card[5];
        for (int i = 0; i < 5; i++)
        {
            cards[i] = new Card { Rank = CommunityRanks[i], Suit = CommunitySuits[i] };
        }
        return cards;
    }

    private Card[][] BuildPlayerCards()
    {
        var result = new Card[2][];
        for (int p = 0; p < 2; p++)
        {
            result[p] = new Card[2];
            for (int c = 0; c < 2; c++)
            {
                result[p][c] = new Card { Rank = PlayerRanks[p][c], Suit = PlayerSuits[p][c] };
            }
        }
        return result;
    }

    private async Task EvaluateAsync()
    {
        ErrorMessage = string.Empty;
        LeftHand = null;
        RightHand = null;
        Comparison = 0;

        // basic duplicate detection: exact same rank+suit used more than once
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allCards = new List<Card>();
        allCards.AddRange(CommunityCards);
        allCards.AddRange(PlayerCards[0]);
        allCards.AddRange(PlayerCards[1]);

        foreach (var c in allCards)
        {
            var key = $"{(int)c.Rank}-{c.Suit}";
            if (used.Contains(key))
            {
                ErrorMessage = "Duplicate card selected. Each card on the board and in players' hands must be unique.";
                return;
            }
            used.Add(key);
        }

        try
        {
            // Call the private evaluator PokerGame.EvaluateHandScore via reflection.
            MethodInfo? evalMethod = typeof(PokerGame).GetMethod("EvaluateHandScore", BindingFlags.NonPublic | BindingFlags.Static);
            if (evalMethod == null)
            {
                ErrorMessage = "Internal evaluator not found (EvaluateHandScore). Cannot evaluate.";
                return;
            }

            // Evaluate left player
            Card[] leftAll = PlayerCards[0].Concat(CommunityCards).ToArray();
            LeftHand = (HandScore?)evalMethod.Invoke(null, new object[] { leftAll });

            // Evaluate right player
            Card[] rightAll = PlayerCards[1].Concat(CommunityCards).ToArray();
            RightHand = (HandScore?)evalMethod.Invoke(null, new object[] { rightAll });

            if (LeftHand is null || RightHand is null)
            {
                ErrorMessage = "Evaluation returned null result.";
                return;
            }

            // Compare using public CompareHandScores
            Comparison = PokerGame.CompareHandScores(LeftHand, RightHand);

            // Force UI update
            await InvokeAsync(StateHasChanged);
        }
        catch (TargetInvocationException tie)
        {
            // unwrap the inner exception that's thrown by the evaluator if it complains
            ErrorMessage = $"Evaluator error: {tie.InnerException?.Message ?? tie.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unexpected error: {ex.Message}";
        }
    }

    // Card image filename helper (same mapping as Home.razor.cs)
    private static string ToCardFile(Card card)
    {
        var rank = card.Rank switch
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

        var suit = card.Suit switch
        {
            CardSuit.Clubs => "clubs",
            CardSuit.Diamonds => "diamonds",
            CardSuit.Hearts => "hearts",
            CardSuit.Spades => "spades",
            _ => throw new ArgumentOutOfRangeException()
        };

        return $"/images/cards/{rank}_of_{suit}.png";
    }
}