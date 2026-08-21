using LostAndDivine.Shared.Models;

namespace LostAndDivine.Tests;

public class ItemInvariantTests
{
    [Fact]
    public void EnhancementLevel_ClampedToValidRange()
    {
        var item = new Item();
        int max = EnhancementHelper.MaxLevel;

        item.EnhancementLevel = max + 50;
        Assert.Equal(max, item.EnhancementLevel);

        item.EnhancementLevel = -10;
        Assert.Equal(0, item.EnhancementLevel);

        item.EnhancementLevel = 3;
        Assert.Equal(3, item.EnhancementLevel);
    }
}
