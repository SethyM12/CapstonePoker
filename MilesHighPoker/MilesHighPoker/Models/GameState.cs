using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Models;

public class GameState
{
    private Deck deck;
    private PokerGame game;

    public Card[] CommunityCards => game.GetCommunityCards();
    public HandStreet CurrentStreet => game.CurrentStreet;

    public uint Pot { get; private set; }
    public uint CurrentBet { get; private set; }
    public uint MinimumRaise { get; private set; }
    public uint BigBlind { get; private set; }

    public short DealerPosition { get; private set; }
    public short CurrentPlayerPosition { get; private set; }
    public short? LastAggressorPosition { get; private set; }

    public uint HandNumber { get; private set; }
    public bool IsHandOver => CurrentStreet == HandStreet.Showdown;

    public GameState()
    {
        deck = new Deck();
        game = new PokerGame(deck);

        Pot = 0;
        CurrentBet = 0;
        MinimumRaise = 0;
        BigBlind = 0;

        DealerPosition = 0;
        CurrentPlayerPosition = 0;
        LastAggressorPosition = null;

        HandNumber = 0;
    }

    public void StartHand(short dealerPosition, uint bigBlind)
    {
        if (bigBlind == 0)
            throw new ArgumentException("Big blind must be a positive value.", nameof(bigBlind));

        HandNumber++;
        deck = new Deck();
        game = new PokerGame(deck);

        Pot = 0;
        CurrentBet = 0;           // Set after blinds are actually posted
        MinimumRaise = bigBlind;  // Min raise increment for this hand
        BigBlind = bigBlind;

        DealerPosition = dealerPosition;
        CurrentPlayerPosition = dealerPosition; // Turn order finalized in Table flow step
        LastAggressorPosition = null;
    }
    
    public void ApplyPostedBlinds(uint bigBlindPosted, short bigBlindSeat)
    {
        if (bigBlindPosted == 0)
            throw new ArgumentException("Big blind posted must be greater than zero.", nameof(bigBlindPosted));

        CurrentBet = bigBlindPosted;
        LastAggressorPosition = bigBlindSeat;

        // Keep minimum raise anchored to configured blind size.
        MinimumRaise = BigBlind;
    }

    public void DealHoleCards(List<Player> players)
    {
        game.Deal(players);
    }

    public void RevealFlop(List<Player> players)
    {
        game.Flop();
        ResetBettingForNewStreet(players);
    }

    public void RevealTurn(List<Player> players)
    {
        game.Turn();
        ResetBettingForNewStreet(players);
    }

    public void RevealRiver(List<Player> players)
    {
        game.River();
        ResetBettingForNewStreet(players);
    }

    public PokerHand GetHandType(Card[] playerCards)
    {
        return game.GetHandType(playerCards);
    }
    
    public HandScore GetHandScore(Card[] playerCards)
    {
        return game.GetHandScore(playerCards);
    }
    

    public void AddToPot(uint amount)
    {
        checked
        {
            Pot += amount;
        }
    }

    public void SetCurrentTurn(short playerPosition)
    {
        CurrentPlayerPosition = playerPosition;
    }

    // newBet is the player's total bet for the current street
    public void RecordAction(uint newBet, short playerPosition)
    {
        if (newBet < CurrentBet)
            throw new ArgumentException("Bet amount cannot decrease.", nameof(newBet));

        uint raiseSize = newBet - CurrentBet;

        // Aggressive action (bet/raise)
        if (raiseSize > 0)
        {
            if (raiseSize < MinimumRaise)
                throw new ArgumentException($"Raise must be at least {MinimumRaise}.", nameof(newBet));

            CurrentBet = newBet;
            MinimumRaise = raiseSize;
            LastAggressorPosition = playerPosition;
            return;
        }

        // Check/call style action: no new aggressor
        CurrentBet = newBet;
    }

    private void ResetBettingForNewStreet(List<Player> players)
    {
        CurrentBet = 0;
        LastAggressorPosition = null;
        MinimumRaise = BigBlind; // opening bet on postflop streets

        foreach (Player player in players)
        {
            player.ResetBetForNewStreet();
        }
    }
}
