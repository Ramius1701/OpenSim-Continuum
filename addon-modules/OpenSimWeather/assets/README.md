# Weather asset guidance

OpenSimWeather configuration uses **asset UUIDs**. A local PNG/WAV filename or
an inventory item name cannot be resolved by the region module.

The bundled `Weather_assets_*.png`, `Weather_assets.pdn`, `ThunderSound.wav`,
and `ThunderSound2.wav` files were imported from
`ManfredAabye/OpenSimWeather` commit
`401fc0ff67a8ed9c2888151d4fdab4bc10fb592d`. The donor identifies Manfred
Zainhofer as the texture author; its permission statement is preserved in
`ManfredAabye-textures-LICENSE.txt`. The module consumes asset UUIDs rather
than these editable source filenames directly.

Viewer-ready JPEG2000 textures and an Ogg Vorbis thunder sound also ship in
`bin/assets/WeatherAssetSet`. OpenSim's default asset loader inserts them into
the standalone or Robust asset service using stable UUIDs, so the supplied
configuration examples work without manual uploads. The PNG/WAV files remain
available as editable source media.

## Particle textures

Recommended source images:

- `RainTexture`: narrow vertical alpha rain streak, soft edges.
- `StormTexture`: heavier/longer alpha rain streak; it may reuse RainTexture.
- `SnowTexture`: white snowflake or soft irregular flake on transparent alpha.
- `LightningTexture`: white branching bolt or soft vertical flash on alpha.

Square source canvases generally behave most predictably as particle textures.
Keep transparent padding modest; excessive padding makes particles appear much
smaller than their configured scale.

Custom replacements may be uploaded and their asset UUIDs placed in the
`[Weather]` section. Every configured UUID must be available through the grid
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

The supplied example configures the bundled sound UUID. `ThunderEnabled` stays
opt-in; enabling it with an empty or invalid UUID leaves visual lightning
enabled but produces no thunder audio.

The donor does not separately identify the recording source for the two bundled
WAV variants, so their exact repository and commit provenance above is retained.

The stable bundled UUIDs are meaningful because the corresponding assets are
part of the default asset set and are inserted into the target asset service.
