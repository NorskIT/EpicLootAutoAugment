# Verification

Target: installed EpicLoot 0.15.5, Valheim 1.0.21, Jotunn 2.30.2.

## Automated checks — 2026-11-01

- Release compilation against the actual installed game/mod assemblies: zero errors or warnings.
- 12 core tests passed: match termination, missed-roll settlement, no duplicate request, late result after cancellation, exact minimum, decimal formats, invalid bounds, valueless effects, non-finite values and restart.
- Finished archive validation passed: safe normalized paths, duplicate entries, root metadata/icon, dependency list, build version and DLL hashes. Ten corrupt/unsafe ZIP fixtures were rejected, including Windows paths, traversal, controls, case-insensitive duplicates, wrong DLL and wrong version.

## Isolated runtime checks

Test harnesses use disposable local characters/worlds and isolated BepInEx directories. Deterministic offered choices are supplied by a separate test-only DLL, excluded from the package; payment, dialog handling and effect application run through the actual game/EpicLoot code.

- `artifacts/smoke-02`: native UI attachment; optional minimum; invalid threshold; valueless effect controls; no-material stop; reject low roll, stop on exact minimum; no extra payment after match; unchanged open native dialog; manual effect application; already-satisfied target costs nothing.
- `artifacts/network-01-client` and `artifacts/network-01/host-result.txt`: client joined a separate local EpicLoot host **without Auto Augment** and completed the augment checks. Transport was an isolated loopback socket through Valheim's real connection/handshake path, not a production or public server.
- `artifacts/final-storage`: the same automation with EpicLoot Container Access 0.1.6 and materials held only in a nearby chest; exact per-attempt deductions; no overlapping actions; manual stop during unpaid countdown; exactly one remaining paid miss followed by material exhaustion; old effect retained.
- `artifacts/final-ui`: 31 runtime assertions passed against the final plugin, including normal inventory payment, cancellation/exhaustion, filtering, wrapped conditional effects and a real UI raycast against the empty right edge of an effect row.
- Actual native Canvas rendered to PNG at 1920×1080, 2560×1440 and 120% UI scale. Reviewed controls, font/texture consistency, bounds and native match dialog. These captures use a plain backdrop because the hidden test instance does not present normal gameplay frames.

Before public release, player acceptance testing should cover the full production mod combination, real mouse/gamepad interaction, translations, remote latency/disconnection and manual Esc at a match. The ordinary result dialog deliberately retains Esc-to-keep behavior. The internal EpicLoot integration is version-gated to 0.15.5; other versions fail closed and retain ordinary augmenting.

No production server, profile, world or character is modified by build/package scripts. Local ZIP validation does not prove Hexium upload acceptance.
