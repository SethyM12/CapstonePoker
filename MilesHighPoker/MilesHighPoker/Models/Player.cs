using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Models;

public sealed class Player
{
    public String Name { get; set; }
    public uint Id { get; set; }
    public uint ConnectionId { get; set; }
    public List<Card> Cards { get; set; } = [];
    public uint Chips { get; set; }
    public short Seat  { get; set; }
    public bool Folded { get; set; }
    public uint Bet { get; set; }
    
    public Player(String name, uint id, uint connectionId, short seat)
    {
        Name = name;
        Id = id;
        ConnectionId = connectionId;
        Seat = seat;
    }
    
    
}