namespace MilesHighPoker.Models;

public class Table
{
    public String  TableId { get; set; }
    public List<WaitingPlayer> WaitingPlayers { get; set; }
}