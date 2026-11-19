# Active donor refresh — 2026-11-19

This refresh fixes the source boundary for the selected stable Tranquillity port;
later commits belong in a separate maintenance delta. It does not merge an entire
experimental branch or reopen completed donor families.

| Repository/ref | Fetched revision | Disposition |
| --- | --- | --- |
| Official OpenSim master | `9d00a5555225b17d3d1063cb0f3496fb1b6217f9` | Already in published Continuum. No new master commits to merge at this refresh. |
| Tranquillity stable master | `9ce6f33d3b3b617479389a896ba1f41d85736eb0` | Five commits beyond release `628bdc7b09`; release merge, docs and packaging only. No further runtime delta. |
| Tranquillity develop | `80a642a681759de7ccb798bb06466a50b099b9dd` | 258 commits beyond the release. Recorded separately; not claimed fully ported or audited by this stable-source closeout. |

Selected newer compatible changes included in this work: the COF-before-login
bake and bounded read (`df2c1575d2`, `b52dca8257`, `1d9a655c29`), explicit source
licence notices, and the owner's already-posted OpenSim submissions
[75](https://github.com/opensim/opensim/pull/75),
[76](https://github.com/opensim/opensim/pull/76),
[77](https://github.com/opensim/opensim/pull/77).
Continuum's native payment finalization, auction guards, group bans and reserved
charges take precedence over older duplicate code in those patches.

The implementation boundary and validation are in
[the Tranquillity ledger](tranquillity-release-review.md). Tests still requiring a
grid/viewer/provider are in [the handoff](donor-feature-test-handoff.md).

## Git integrity finding

The first OpenSim fetch reported an existing packed-object CRC error during
automatic geometric repack:

```text
object: 539a802003df60a7549dbc75f0722b13807c52b1
pack: S:/Github/OpenSim-Continuum/.git/objects/pack/pack-72753ea7c2904e43bd296c11be7a786289629da5.pack
```

Repeating the fetch with maintenance.auto=false and gc.auto=0 succeeded, and the
remote heads above were verified. Release builds, source diffs and tests passed.
No pack repair, history rewrite, pruning, reset, staging, commit or push was done.
Publication is isolated in a fresh, independent clone at
S:/Github/OpenSim-Continuum-publish-tranquillity. That clone passed
git fsck --full --strict and does not share or reuse the damaged object store.
The original integrity issue remains recorded for separate repair; all existing
dirty checkouts and worktrees remain untouched by publication.
