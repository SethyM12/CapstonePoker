namespace MilesHighPoker.Models;

public record WaitingPlayer(
    String ConnectionId,
    String Name,
    DateTime JoinedDate
);