# Changelog

## [0.1.0]

### Added
- Package `com.sevalkr.cloudsave` ("SK Cloud Save Kit"), shipping the `SK.CloudSave` and
  `SK.CloudSave.Editor` assemblies under the `SK.CloudSave.*` namespaces.
- Binary save envelope (`CSK1`) carrying schema version, UTC timestamp, total playtime
  and writer id, protected by CRC-32 corruption detection.
- `CloudSaveManager`: local-first orchestration with pending-upload queue, automatic
  reconciliation on load, `SyncAsync`/`SyncAllAsync`, and per-slot operation locking.
- Pluggable deterministic conflict resolution: `LongestPlaytimeResolver` (recommended),
  `LastWriteWinsResolver`, `DelegateConflictResolver`.
- Providers: Google Play Saved Games (manual conflict resolution routed through the
  shared resolver), iCloud key-value store (no Game Center sign-in required),
  file-backed fake cloud for the editor, in-memory test doubles.
- Automatic Xcode entitlement setup for iCloud key-value storage on iOS builds.
- EditMode test suite covering envelope integrity, resolvers, sync scenarios and the local store.

### Known limitations
- iCloud key-value store enforces Apple's 1 MB total limit; larger saves need compression
  or a future iCloud Documents provider.

