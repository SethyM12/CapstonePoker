using MilesHighPoker.GameLogic;

namespace MilesHighPoker.Components.Pages;

public partial class Home
{
    private Card?[] CommunityCards { get; set; } = new Card[5];
    private uint Pot { get; set; } = 0;
}