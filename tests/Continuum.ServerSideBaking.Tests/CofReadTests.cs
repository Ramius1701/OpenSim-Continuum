using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.OptionalModules.Avatar.ServerSideBaking;
using OpenSim.Services.Interfaces;
using Xunit;

namespace OpenSim.Region.OptionalModules.ServerSideBaking.Tests;

public class CofReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReaderChecksTheLinkedItemsOwner(bool foreign)
    {
        UUID owner = UUID.Random(), folderID = UUID.Random(), targetID = UUID.Random();
        var folder = new InventoryFolderBase(folderID, "COF", owner, (short)FolderType.CurrentOutfit, UUID.Random(), 1);
        var link = new InventoryItemBase(UUID.Random(), owner)
        { Folder = folderID, AssetID = targetID, AssetType = (int)AssetType.Link, InvType = (int)InventoryType.Wearable, Description = "@400" };
        var target = new InventoryItemBase(targetID, foreign ? UUID.Random() : owner)
        { AssetType = (int)AssetType.Clothing, InvType = (int)InventoryType.Wearable, Flags = (uint)WearableType.Shirt, AssetID = UUID.Random() };
        var inventory = NativeFixtureProxy.Create<IInventoryService>((m, a) => m.Name switch
        {
            "GetFolderForType" => folder,
            "GetFolderContent" => new InventoryCollection { Items = new List<InventoryItemBase> { link } },
            "GetItem" => target,
            _ => null
        });
        object read = Read(inventory, owner, CancellationToken.None)!;
        var links = (List<CofWearableLink>)read.GetType().GetProperty("Links")!.GetValue(read)!;
        Assert.Equal(foreign ? 0 : 1, links.Count);
    }

    [Fact]
    public void ACancelledReadCannotReturnAnOutfitToApplyLate()
    {
        UUID owner = UUID.Random(), folder = UUID.Random();
        int lookups = 0;
        var inventory = NativeFixtureProxy.Create<IInventoryService>((m, a) =>
        {
            if (m.Name == "GetFolderForType") return new InventoryFolderBase(folder, "COF", owner, (short)FolderType.CurrentOutfit, UUID.Random(), 1);
            if (m.Name == "GetFolderContent") return new InventoryCollection
            { Items = new List<InventoryItemBase> { new(UUID.Random(), owner) { Folder = folder, AssetType = (int)AssetType.Link } } };
            if (m.Name == "GetItem") lookups++;
            return null;
        });
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Null(Read(inventory, owner, cancellation.Token));
        Assert.Equal(0, lookups);
    }

    private static object? Read(IInventoryService inventory, UUID owner, CancellationToken token) =>
        typeof(ServerSideBakingModule).GetMethod("ReadCof", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { inventory, owner, token });
}
