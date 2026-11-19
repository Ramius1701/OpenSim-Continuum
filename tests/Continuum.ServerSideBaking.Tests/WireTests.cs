using System.Reflection;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Region.OptionalModules.Avatar.ServerSideBaking;
using Xunit;

namespace OpenSim.Region.OptionalModules.ServerSideBaking.Tests;

/// <summary>
/// The wire. The §4.3 COF handshake in all four of its branches, the per-region flag, and the one
/// thing ADR-001 will not tolerate: that a flag-off region's <c>AvatarAppearance</c> packet is byte-for-byte the
/// packet it was before server-side baking existed.
/// </summary>
public class WireTests
{
    private static readonly UUID Agent = new("a7d2ff2e-dc32-44d8-aa61-3d22070a4964");
    private static readonly DateTime T0 = new(2026, 9, 4, 21, 0, 0, DateTimeKind.Utc);

    private static int NeverCalled() => throw new InvalidOperationException("the folder must not be re-read on this branch");

    // ------------------------------------------------------------------ the four handshake branches

    [Fact]
    public void Handshake_Equal_Bakes()
    {
        var h = new CofHandshake();
        var d = h.Decide(Agent, clientVersion: 7, serverVersion: 7, NeverCalled, T0);

        Assert.Equal(CofVerdict.Bake, d.Verdict);
        Assert.True(d.Success);
        Assert.Equal(7, d.Version);
        Assert.Equal(0, h.MismatchesFor(Agent));
    }

    [Fact]
    public void Handshake_ClientBehind_IsStaleAndQuotesTheServersVersion()
    {
        var h = new CofHandshake();
        var d = h.Decide(Agent, clientVersion: 5, serverVersion: 9, NeverCalled, T0);

        Assert.Equal(CofVerdict.Stale, d.Verdict);
        Assert.False(d.Success);
        Assert.Equal(9, d.Version);           // this is the `expected` the viewer re-requests with
        Assert.Equal(1, h.MismatchesFor(Agent));
    }

    /// <summary>
    /// The viewer is ahead because it changed the COF by a path this sim has not seen — with AIS live, that is
    /// the normal case. The folder is re-read once, and if the write has landed by then it is a plain match.
    /// </summary>
    [Fact]
    public void Handshake_ClientAhead_RereadsOnceAndBakesWhenItCatchesUp()
    {
        var h = new CofHandshake();
        var rereads = 0;
        int Reread() { rereads++; return 11; }

        var d = h.Decide(Agent, clientVersion: 11, serverVersion: 9, Reread, T0);

        Assert.Equal(1, rereads);
        Assert.Equal(CofVerdict.Bake, d.Verdict);
        Assert.Equal(11, d.Version);
        Assert.Equal(0, h.MismatchesFor(Agent));
    }

    /// <summary>
    /// Still ahead after the re-read: the sim cannot honestly claim the viewer's number, so it quotes the freshly
    /// read one and lets the viewer come back. Exactly one re-read, never a loop of them.
    /// </summary>
    [Fact]
    public void Handshake_ClientStillAheadAfterTheReread_IsStaleWithTheFreshVersion()
    {
        var h = new CofHandshake();
        var rereads = 0;
        int Reread() { rereads++; return 10; }

        var d = h.Decide(Agent, clientVersion: 12, serverVersion: 9, Reread, T0);

        Assert.Equal(1, rereads);
        Assert.Equal(CofVerdict.Stale, d.Verdict);
        Assert.Equal(10, d.Version);          // the re-read value, not the stale 9
        Assert.Equal(1, h.MismatchesFor(Agent));
    }

    [Fact]
    public void Handshake_ARereadThatThrows_DoesNotEscapeAndStillAnswers()
    {
        var h = new CofHandshake();
        var d = h.Decide(Agent, clientVersion: 12, serverVersion: 9, () => throw new TimeoutException("inventory service"), T0);

        Assert.Equal(CofVerdict.Stale, d.Verdict);
        Assert.Equal(9, d.Version);
    }

    // ------------------------------------------------------------------ anti-livelock

    [Fact]
    public void Handshake_TooManyMismatchesInTheWindow_BakesAnyway()
    {
        var h = new CofHandshake { MaxMismatches = 3, Window = TimeSpan.FromSeconds(30) };

        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0).Verdict);
        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(1)).Verdict);

        var third = h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(2));
        Assert.Equal(CofVerdict.LivelockBake, third.Verdict);
        Assert.True(third.Success);
        Assert.Equal(9, third.Version);                 // baked at the server's version, per §4.3
        Assert.Contains("3 mismatches", third.Reason);

        // and it starts over afterwards, so the next disagreement is refused normally again
        Assert.Equal(0, h.MismatchesFor(Agent));
        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(3)).Verdict);
    }

    [Fact]
    public void Handshake_MismatchesOutsideTheWindowDoNotAccumulate()
    {
        var h = new CofHandshake { MaxMismatches = 3, Window = TimeSpan.FromSeconds(30) };

        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0).Verdict);
        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(10)).Verdict);
        // past the window: the count restarts rather than tripping the rule
        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(41)).Verdict);
        Assert.Equal(1, h.MismatchesFor(Agent));
        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(42)).Verdict);
    }

    [Fact]
    public void Handshake_ASuccessfulBakeClearsTheAgentsCounter()
    {
        var h = new CofHandshake { MaxMismatches = 3 };
        h.Decide(Agent, 5, 9, NeverCalled, T0);
        h.Decide(Agent, 5, 9, NeverCalled, T0.AddSeconds(1));
        Assert.Equal(2, h.MismatchesFor(Agent));

        Assert.Equal(CofVerdict.Bake, h.Decide(Agent, 9, 9, NeverCalled, T0.AddSeconds(2)).Verdict);
        Assert.Equal(0, h.MismatchesFor(Agent));
    }

    [Fact]
    public void Handshake_CountersArePerAgent()
    {
        var h = new CofHandshake { MaxMismatches = 2 };
        var other = UUID.Random();

        Assert.Equal(CofVerdict.Stale, h.Decide(Agent, 5, 9, NeverCalled, T0).Verdict);
        Assert.Equal(CofVerdict.Stale, h.Decide(other, 5, 9, NeverCalled, T0).Verdict);
        Assert.Equal(1, h.MismatchesFor(Agent));
        Assert.Equal(1, h.MismatchesFor(other));
    }

    // ------------------------------------------------------------------ the per-region flag

    private static IConfigSource Config(params (string Section, string Key, string Value)[] entries)
    {
        var src = new IniConfigSource();
        foreach (var (section, key, value) in entries)
            (src.Configs[section] ?? src.AddConfig(section)).Set(key, value);
        return src;
    }

    [Theory]
    // simulatorDefault, region section, region value  ->  expected
    [InlineData(false, null, null, false)]              // nothing anywhere: off, which is what every shipped ini says
    [InlineData(true, null, null, true)]                // simulator-wide on, no region section
    [InlineData(false, "Ebony", "true", true)]          // one region opts in
    [InlineData(true, "Ebony", "false", false)]         // and a region can opt out of a simulator-wide on
    [InlineData(false, "Elm", "true", false)]           // a section for a different region does not apply
    public void FlagResolvesPerRegionLikeAisEnabledDoes(bool simulatorDefault, string? section, string? value, bool expected)
    {
        var config = section is null ? Config() : Config((section, "ServerSideBaking", value));
        Assert.Equal(expected, ServerSideBakingRegion.ResolveEnabled(simulatorDefault, config, "Ebony"));
    }

    [Fact]
    public void FlagFallsBackToTheSimulatorDefaultWhenThereIsNoConfigOrNoRegionName()
    {
        Assert.True(ServerSideBakingRegion.ResolveEnabled(true, null, "Ebony"));
        Assert.False(ServerSideBakingRegion.ResolveEnabled(false, null, "Ebony"));
        Assert.True(ServerSideBakingRegion.ResolveEnabled(true, Config(("Ebony", "ServerSideBaking", "false")), null));
    }

    // ------------------------------------------------------------------ what the flag gates

    [Fact]
    public void AFlagOffRegionNeverReportsABakedCofVersion()
    {
        var off = new ServerSideBakingRegion(false, new CofHandshake());
        Assert.False(off.ServerSideBakingEnabled);
        Assert.Equal(-1, off.BakedCofVersion(Agent));

        // a console bake on a flag-off region still writes faces and sends, but must not reach the wire
        off.RecordBake(Agent, 42);
        Assert.Equal(-1, off.BakedCofVersion(Agent));
    }

    [Fact]
    public void AFlagOnRegionReportsTheVersionItBakedAtAndForgetsOnClose()
    {
        var on = new ServerSideBakingRegion(true, new CofHandshake());
        Assert.Equal(-1, on.BakedCofVersion(Agent));

        on.RecordBake(Agent, 42);
        Assert.Equal(42, on.BakedCofVersion(Agent));

        on.RecordBake(Agent, 43);
        Assert.Equal(43, on.BakedCofVersion(Agent));

        // a negative version is "no bake", never recorded
        on.RecordBake(Agent, -1);
        Assert.Equal(43, on.BakedCofVersion(Agent));

        on.Forget(Agent);
        Assert.Equal(-1, on.BakedCofVersion(Agent));
        Assert.Equal(0, on.Handshake.MismatchesFor(Agent));
    }

}
