# Server-side appearance baking

Continuum can compose classic and Bakes-on-Mesh avatar textures on the simulator.
It reuses the native asset service and avatar key/value store. Client baking is
still the default. Do the disposable-grid checks in donor-feature-test-handoff.md
before enabling server baking on a public grid.

## Grid configuration

On ROBUST, enable the public appearance connector in `[ServiceList]`:

```ini
AppearanceServiceConnector = "${Const|PublicPort}/OpenSim.Server.Handlers.dll:AppearanceServerConnector"
```

The supplied ROBUST examples contain the corresponding `[AppearanceService]`
section. It loads the native avatar and asset services using the grid's existing
database settings. If the grid uses another asset implementation, use that same
implementation here; do not point the appearance service at a different store.

In ROBUST's `[LoginService]`, set the externally reachable base URL:

```ini
AgentAppearanceServiceURL = "https://YOUR-GRID-PUBLIC-URL/"
```

The trailing slash is normalized by the login service. Both XML-RPC and LLSD
login replies advertise it as `agent_appearance_service`. The viewer fetches
`texture/<agent UUID>/<channel>/<asset UUID>` beneath that URL. A reverse proxy
must route this path to the public appearance connector. Requests must identify
the exact texture indexed for that agent and channel; missing or mismatched
textures return 404.

On a simulator, opt in only after that endpoint is available:

```ini
[Appearance]
ServerSideBaking = true
BakeSize = 1024
BakeQuality = 0.85
```

Supported sizes are 512, 1024 and 2048. A section named for a region can override
`ServerSideBaking` for that region. Enabling it tells Firestorm to use server
baking there, so the public endpoint is required. AIS is separately controlled
by `[AIS] ExperimentalEnabled`; server baking does not enable it implicitly.

For a standalone, use the provided `[AppearanceService]` section in
StandaloneCommon.ini.example, set `[Modules] AppearanceServiceInConnector=true`,
and set `[LoginService] AgentAppearanceServiceURL` to the standalone's public
base URL. This hosts the same appearance connector on the simulator HTTP server.

## Operation and recovery

Login/root-agent arrival and confirmed appearance saves queue a bake. Login
reads the Current Outfit first, with a five-second limit and stored-record
fallback; a late read cannot apply after the bake has proceeded. The
`UpdateAvatarAppearance` capability checks the Current Outfit folder version
and resolves wearable links before queuing a save. Same-type clothing follows
the viewer's `@` ordering. Physics and Universal wearable slots survive native
avatar persistence, including a resized wearable array on readback.

The simulator caches per-channel input hashes in avatar rows beginning `Bake`.
An unchanged channel can reuse its stored texture. Appearance saves preserve
those rows, and per-agent service locks keep a local save from racing an index
write. Bake jobs for the same resident are serialized in this process.

New textures are stored before the avatar index is written and read back. If
the index is not confirmed, the existing appearance is restored and old assets
are retained; newly stored unreferenced assets may remain. A partially failing
external provider is not made transactional by this feature. Confirm provider
behavior during acceptance. Successful replacement can remove superseded bakes.

`appearance serverbake <first-name> <last-name>` requests a bake for a root agent
and prints channel results. It differs from the existing viewer rebake command.
Disabling the region option returns viewers to client baking after relog.

The build includes managed Skia/CoreJ2K dependencies and native Skia binaries in
`bin/runtimes`. Keep that directory with the distribution. The compositor has
an explicit native resolver because OpenSim loads plugins from a shared bin.

## Provenance and validation boundary

Adapted from Tranquillity release v1.1.2, `628bdc7b09`, with Continuum's existing
.NET 8, logging, plugin discovery, CAPS and service APIs. The compositor and
viewer data notices remain alongside the source; additional licence texts are
under ThirdParty/AppearanceBaking. Its namespace retains the donor name for
resource compatibility. No replacement script or physics engine is required.

Automated tests cover reference pixel vectors, native JPEG 2000 encode/decode,
synthetic BOM channels, wearable persistence/order, bake-index preservation and
refusal, service lookup, COF handshake, HTTP request bounds and capability
teardown. Captured real-outfit reference tests explicitly skip when their private
assets are absent. These results do not establish live viewer, Linux/macOS,
multi-simulator or database-provider acceptance.
