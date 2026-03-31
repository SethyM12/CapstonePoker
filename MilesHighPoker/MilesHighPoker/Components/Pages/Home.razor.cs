namespace MilesHighPoker.Components.Pages;

public partial class Home
{
    private readonly string?[] CommunityCards = new string?[5];

    private void SetCommunityCard(int slot, string card)
    {
        if (slot < 0 || slot >= CommunityCards.Length) return;
        CommunityCards[slot] = card;
    }

    private void ClearCommunityCards()
    {
        for (var i = 0; i < CommunityCards.Length; i++)
            CommunityCards[i] = null;
    }
}