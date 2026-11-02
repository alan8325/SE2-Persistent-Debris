# Persistent Debris Implementation Plan

**Goal:** Keep ship-block debris physically active until more than 100 pieces exist, then despawn the oldest as new pieces arrive.

**Architecture:** Use VRAGE3 partial definition overrides to pause the two block-debris timeout components. Pin the built-in `MaxActiveDebris` queue to 100, preserving its oldest-first replacement and all physics definitions.

**Tech stack:** VRAGE3 Mod HUB/Editor, JSON partial definitions, Python stdlib unittest, SE2 in-game verification.

**Spec:** User request in conversation; no separate spec file.

## Global Constraints

- Do not edit vanilla game or SDK files directly.
- Default global debris cap: 100.
- Preserve rigid-body, gravity, and motion settings.
- Cover fractured-block and damaged non-breakable-block debris.
- Leave voxel debris, loose items, backpacks, and other cleanup systems unchanged.
- Preserve collision-conflict cleanup safeguards.

## Review Focus

- Both debris prefabs must be covered, not only fractured debris.
- Timeout overrides must not replace physics components.
- The cap must remain oldest-first and set to exactly 100.
- Vanilla cleanup for unrelated entity types must remain unchanged.

### Task 1: Disable age-based block-debris cleanup

Create override partial definitions for base GUIDs `3cc73a40-9e48-43a9-885f-1c28bd5b824a` and `bef7011b-c06a-4be2-bf10-debc5bfe300c`, setting only their existing timeout builders' `TimeoutPaused` to `true`. Build with the real SDK and verify the compiled definitions.

### Task 2: Pin the cap and verify behavior

Override base GUID `70563a82-6a30-4a58-af86-6b79f2ec8777` with `MaxActiveDebris = 100`, preserving all other settings. Run the automated suite, then verify persistence, physics, oldest-first replacement, and unrelated cleanup in SE2.
