using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.OptionalModules.Avatar.ServerSideBaking;
using OpenSimNGC.Appearance.Baking;
using Xunit;

namespace OpenSim.Region.OptionalModules.ServerSideBaking.Tests;

public class PersistenceFailureTests
{
    private sealed class RefusingAvatars : FakeAvatarService
    {
        public int Failure;
        public override bool SetItems(UUID owner, string[] names, string[] values) => Failure switch
        {
            1 => false,
            2 => true, // A provider acknowledgement without persistence must also be refused.
            3 => throw new IOException("injected avatar outage"),
            _ => base.SetItems(owner, names, values)
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedIndexKeepsTheOldAppearanceAndAsset(int failure)
    {
        UUID owner = UUID.Random(), oldHead = UUID.Random();
        var (assets, worn, _) = BomAuxChannelTests.Outfit();
        assets.Put(new AssetBase(oldHead, "previous head", (sbyte)AssetType.Texture, owner.ToString()) { Data = new byte[] { 1 } });
        var avatars = new RefusingAvatars();
        Assert.True(BakeIndex.Write(avatars, owner,
            new[] { new KeyValuePair<BakeChannel, StoredBake>(BakeChannel.Head, new StoredBake(oldHead, "old hash")) }, 1, 32, DateTime.UtcNow));
        avatars.Failure = failure;
        var appearance = new AvatarAppearance();
        appearance.Texture.CreateFace(8).TextureID = oldHead;
        var compositor = new AvatarBakeCompositor();
        var outcome = BakeOrchestrator.Run(owner, BakeReason.CofChanged, worn, null, appearance, assets, avatars,
            new SkiaBakeBackend(compositor) { Quality = 0.5 }, compositor, 32, 2, CancellationToken.None);
        Assert.False(outcome.IndexWritten);
        Assert.DoesNotContain(outcome.Channels, c => c.Status is ChannelStatus.Baked or ChannelStatus.Reused);
        Assert.Equal(oldHead, appearance.Texture.FaceTextures[8].TextureID);
        Assert.NotNull(assets.Get(oldHead.ToString()));
        Assert.Empty(outcome.Superseded);
    }
}
