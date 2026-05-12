using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Models;

public sealed class UiTableState
{
    public String TableId { get; set; } = String.Empty;

    public bool IsHandRunning { get; set; }

    public HandStreet Street { get; set; } = HandStreet.PreDeal;

    public uint Pot { get; set; }

    public uint CurrentBet { get; set; }
    
    public uint MinimumRaise {  get; set; }

    public short DealerSeat { get; set; }

    public short CurrentTurnSeat { get; set; }

    public short LocalSeat { get; set; } = -1;

    public Card?[] CommunityCards { get; set; } = new Card?[5];

    public Card?[] LocalHoleCards { get; set; } = new Card?[2];

    public List<UiSeatState> Seats { get; set; } = [];
}

public sealed class UiSeatState
{
    public short Seat { get; set; }

    public String ConnectionId { get; set; } = String.Empty;

    public String Name { get; set; } = String.Empty;

    public uint Chips { get; set; }

    public uint Bet { get; set; }

    public bool Folded { get; set; }

    public bool IsAllIn { get; set; }

    public bool IsLocalPlayer { get; set; }

    public Card?[] HoleCards { get; set; } = new Card?[2];
}