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
        DealerPosition = 0;
        CurrentPlayerPosition = 0;
        LastAggressorPosition = null;
        HandNumber = 0;
    }

    public void StartHand(short dealerPosition, uint bigBlind)
    {
        if (bigBlind == 0) throw new ArgumentException("Big blind must be a positive value.", nameof(bigBlind));

        HandNumber++;
        deck = new Deck();
        game = new PokerGame(deck);

        Pot = 0;
        CurrentBet = bigBlind;
        MinimumRaise = bigBlind;
        DealerPosition = dealerPosition;
        CurrentPlayerPosition = dealerPosition;
        LastAggressorPosition = null;
    }

    public void DealHoleCards(List<Player> players)
    {
        game.Deal(players);
    }

    public void RevealFlop()
    {
        game.Flop();
        ResetBettingForNewStreet();
    }

    public void RevealTurn()
    {
        game.Turn();
        ResetBettingForNewStreet();
    }

    public void RevealRiver()
    {
        game.River();
        ResetBettingForNewStreet();
    }

    public PokerHand GetHandType(Card[] playerCards)
    {
        return game.GetHandType(playerCards);
    }

    public void AddToPot(uint amount)
    {
        Pot += amount;
    }

    public void SetCurrentTurn(short playerPosition)
    {
        CurrentPlayerPosition = playerPosition;
    }

    public void RecordAction(uint newBet, short playerPosition)
    {
        if (newBet < CurrentBet) throw new ArgumentException("Bet amount cannot decrease.", nameof(newBet));

        uint raiseSize = newBet - CurrentBet;
        if (raiseSize > 0) 
            MinimumRaise = raiseSize;

        CurrentBet = newBet;
        LastAggressorPosition = playerPosition;
    }

    private void ResetBettingForNewStreet()
    {
        CurrentBet = 0;
        LastAggressorPosition = null;
    }
}
