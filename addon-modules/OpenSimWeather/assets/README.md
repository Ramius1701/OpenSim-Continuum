# Weather asset guidance

OpenSimWeather configuration uses **asset UUIDs**. A local PNG/WAV filename or
an inventory item name cannot be resolved by the region module.

## Particle textures

Recommended source images:

- `RainTexture`: narrow vertical alpha rain streak, soft edges.
- `StormTexture`: heavier/longer alpha rain streak; it may reuse RainTexture.
- `SnowTexture`: white snowflake or soft irregular flake on transparent alpha.
- `LightningTexture`: white branching bolt or soft vertical flash on alpha.

Square source canvases generally behave most predictably as particle textures.
Keep transparent padding modest; excessive padding makes particles appear much
smaller than their configured scale.

Upload each texture to the grid, obtain its asset UUID, and place the UUID in
the `[Weather]` section. The UUID must be available to viewers through the grid
asset service.

Blank particle texture settings use OpenSim's blank texture. This is safe but
visually crude—especially for snow, which appears as square billboards.

## Surface textures

- `PuddleTexture`: irregular puddle shape with feathered transparent edges;
  avoid a solid circular alpha mask.
- `SnowSurfaceTexture`: mottled/feathered white snow coverage with transparent
  breakup around the edge.

Surface effects automatically disable an individual effect when its required
texture is blank. This prevents the blank texture from producing a conspicuous
solid disk.

## Thunder sound

Upload a thunder sound supported by the grid/viewer and configure its asset UUID
as `ThunderSound`. `ThunderEnabled = true` with an empty or invalid UUID leaves
visual lightning enabled but produces no thunder audio.

This package deliberately does not ship invented or grid-specific UUIDs. Asset
UUIDs are only meaningful when the corresponding assets actually exist in the
target grid's asset service.
