using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.World.Land;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Xunit;

namespace OpenSim.Region.OptionalModules.ServerSideBaking.Tests;

public class LandValidationTests
{
    private sealed class Rig
    {
        public readonly UUID Seller = UUID.Random(), Buyer = UUID.Random(), Group = UUID.Random();
        public readonly Scene Scene = new(new RegionInfo { RegionID = UUID.Random() });
        public readonly LandManagementModule Module = new();
        public readonly LandData Data;
        public ulong GroupPowers;
        public int Transfers, Deeds;
        public Rig()
        {
            Data = new LandData { LocalID = 1, OwnerID = Seller, GroupID = Group, Flags = (uint)ParcelFlags.ForSale };
            Scene.RegionInfo.RegionSettings.AllowLandResell = true;
            Scene.Permissions.OnIsAdministrator += _ => false;
            Scene.RegisterModuleInterface(NativeFixtureProxy.Create<IGroupsModule>((m, a) =>
                m.Name == "GetMembershipData" ? new GroupMembershipData { GroupPowers = GroupPowers } : null));
            var land = NativeFixtureProxy.Create<ILandObject>((m, a) =>
            {
                if (m.Name == "get_LandData") return Data;
                if (m.Name == "UpdateLandSold") Transfers++;
                if (m.Name == "DeedToGroup") Deeds++;
                return null;
            });
            typeof(LandManagementModule).GetField("m_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Module, Scene);
            var parcels = (Dictionary<int, ILandObject>)typeof(LandManagementModule).GetField("m_landList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Module)!;
            parcels[1] = land;
        }
        public EventManager.LandBuyArgs Purchase(int price = 0, bool group = false) =>
            new(Buyer, group ? Group : UUID.Zero, true, group, false, 1, 256, price, true);
    }

    [Theory]
    [InlineData((uint)ParcelFlags.ForSale, true)]
    [InlineData((uint)ParcelFlags.SellParcelObjects, false)]
    [InlineData((uint)ParcelFlags.ForSaleObjects, false)]
    [InlineData(0u, false)]
    public void OnlyTheSaleFlagAllowsPurchase(uint flags, bool allowed)
    {
        var r = new Rig(); r.Data.Flags = flags;
        var purchase = r.Purchase();
        r.Module.EventManagerOnValidateLandBuy(this, purchase);
        Assert.Equal(allowed, purchase.landValidated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupPurchaseNeedsTheBuyersDeedPower(bool allowed)
    {
        var r = new Rig { GroupPowers = allowed ? (ulong)OpenMetaverse.GroupPowers.LandDeed : 0 };
        var purchase = r.Purchase(group: true);
        r.Module.EventManagerOnValidateLandBuy(this, purchase);
        Assert.Equal(allowed, purchase.landValidated);
    }

    [Fact]
    public void EstateResaleAndAuctionProtectionsRemainInForce()
    {
        var r = new Rig(); r.Scene.RegionInfo.RegionSettings.AllowLandResell = false;
        var purchase = r.Purchase();
        r.Module.EventManagerOnValidateLandBuy(this, purchase);
        Assert.False(purchase.landValidated);
        r.Scene.RegionInfo.RegionSettings.AllowLandResell = true;
        r.Data.AuctionID = 7;
        r.Module.EventManagerOnValidateLandBuy(this, purchase);
        Assert.False(purchase.landValidated);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(10, 0, false)]
    [InlineData(10, 9, false)]
    [InlineData(10, 10, true)]
    [InlineData(10, 11, false)]
    public void NativePaymentFinalizationRequiresTheCommittedPrice(int price, int paid, bool transfer)
    {
        var r = new Rig(); var purchase = r.Purchase(price);
        purchase.landValidated = purchase.economyValidated = true;
        r.Scene.EventManager.OnLandBuyFinalized += r.Module.EventManagerOnLandBuy;
        r.Scene.EventManager.OnLandBuy += (_, e) => e.amountDebited = paid;
        r.Scene.EventManager.TriggerLandBuy(this, purchase);
        Assert.Equal(transfer ? 1 : 0, r.Transfers);
    }

    [Fact]
    public void DeedingCannotSubstituteAnotherGroup()
    {
        var r = new Rig();
        var client = NativeFixtureProxy.Create<IClientAPI>((m, a) => m.Name == "get_AgentId" ? r.Seller : null);
        r.Module.ClientOnParcelDeedToGroup(1, UUID.Random(), client);
        Assert.Equal(0, r.Deeds);
    }
}

public class NativeFixtureProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Callback = null!;
    public static T Create<T>(Func<MethodInfo, object?[], object?> callback) where T : class
    {
        var result = DispatchProxy.Create<T, NativeFixtureProxy>();
        ((NativeFixtureProxy)(object)result).Callback = callback;
        return result;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var result = Callback(method!, args!);
        return result ?? (method!.ReturnType.IsValueType && method.ReturnType != typeof(void)
            ? Activator.CreateInstance(method.ReturnType) : null);
    }
}
