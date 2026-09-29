# Shrine expansion and hedge closures

Felucca, Z0. Staged by Shrine.cs from the current shared client map.

- Hedge additions: (1876,926), (1876,925), (1844,913), (1844,915), (1893,940), (1894,940), (1895,940), (1828,965), (1828,966), (1828,967).
- Shrine clearing: X1792–1798, Y927–933, exactly 7×7 around (1795,930). Existing hedges within the footprint are removed. Stone paving and corner crystal balls/lights match the other magical POIs. Existing approach corridors remain open.
- The shrine's existing spawner and all other spawner/gate definitions are unchanged.
- Three earlier hedge tiles from V6 remain world-saved items; these ten new tiles are map statics.
- ArborShrineDeployment removes exactly four obsolete V6 shrine decorations only after movement validation. It verifies all named maze encounter and POI-gate locations remain reachable and that all ten new walls block movement, then saves normally.
- Backup: Distribution/Backups/ArborMazeV7-20260928. The staging utility verifies terrain and statics outside the footprint are unchanged.
