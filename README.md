# CloudSaveKit

Unified cloud saves for Unity, **Google Play Saved Games** on Android, **iCloud** on
iOS, behind one small API, with the parts every game actually needs:

- **Deterministic, pluggable conflict resolution.** When two devices disagree, a single
  resolver decides, the same one, with the same result, on every platform. Ships with
  playtime based and last-write-wins policies; write your own in a few lines.
- **Local first offline sync.** Saving never fails because of the network. Every save
  lands on disk immediately; cloud upload is best effort with a pending queue that
  retries automatically on the next save, load or sync.
- **Corruption detection.** Every save is wrapped in a CRC-32-checked envelope. A
  truncated or bit-rotted save is treated as absent and the healthy copy (local or
  cloud) wins, corrupted bytes are never handed to your deserializer.
- **No sign-in friction on iOS.** Uses the iCloud key-value store, which works for
  every user with an iCloud account, no Game Center prompt.
- **Automated Xcode setup.** A build post-processor adds the iCloud entitlement to the
  generated project; no manual capability clicking per build.
- **A real test suite.** The core is pure C# with zero UnityEngine dependencies, so its
  tests run on plain .NET in CI, on every push, with no Unity license.

```csharp
using SK.CloudSave;
using SK.CloudSave.Unity;

var saves = CloudSaveKitFactory.CreateDefault();   // platform-appropriate provider
await saves.InitializeAsync();                      // sign-in / availability check

await saves.SaveAsync("main", payloadBytes, totalPlaytime);
LoadReport load = await saves.LoadAsync("main");    // reconciles local vs cloud
```

## Installation

Install via the Unity Package Manager using a Git URL:

```
https://github.com/sevalkr/cloudsavekit.git
```

The package installs as `com.sevalkr.cloudsave` ("SK Cloud Save Kit" in the Package
Manager). Requires Unity **2021.3** or newer.

Public API lives in the `SK.CloudSave` namespace; providers, Unity glue and editor
tooling in `SK.CloudSave.Providers`, `SK.CloudSave.Unity` and `SK.CloudSave.Editor`.
The assemblies are `SK.CloudSave` and `SK.CloudSave.Editor`. Reference those from your
own asmdefs.

### Android (Google Play Saved Games)

1. Install the official [Google Play Games plugin v2](https://github.com/playgameservices/play-games-plugin-for-unity)
   (0.11.x or newer).
   - **UPM install** (`com.google.play.games`): nothing else to do, CloudSaveKit
     detects the package and defines `CLOUDSAVEKIT_GPGS` automatically.
   - **.unitypackage install**: add `CLOUDSAVEKIT_GPGS` to
     *Project Settings → Player → Scripting Define Symbols* for Android. If the plugin
     version you use ships without an assembly definition, also add one named
     `Google.Play.Games` to its folder (or place CloudSaveKit inside `Assets/` instead
     of `Packages/`).
2. In the [Google Play Console](https://play.google.com/console), enable
   *Play Games Services* for your app and turn on **Saved Games** in the configuration.
3. Follow the plugin's own setup (resources from the console, `PlayGamesPlatform` setup).

### iOS / tvOS / visionOS (iCloud)

1. Nothing to configure in Unity, the included build post-processor adds the iCloud
   key-value storage entitlement to the Xcode project automatically.
2. In the [Apple Developer portal](https://developer.apple.com/account), enable the
   **iCloud** capability for your App ID.

That's it. On unsupported platforms (or when the user isn't signed in) CloudSaveKit
degrades gracefully to a robust local-only save system, your game code doesn't branch.

## Core concepts

### The envelope

CloudSaveKit never stores your payload raw. It wraps it in a small binary envelope:

```
magic "CSK1" · format version · schema version · UTC timestamp ·
total playtime · writer id · payload · CRC-32
```

- **Schema version** is yours: bump `CloudSaveOptions.SchemaVersion` when your save
  format changes, and read `LoadReport.Metadata.SchemaVersion` to migrate old saves.
- **Total playtime** is the strongest conflict signal a progression game has. Unlike
  wall clocks it never goes backwards, so it's immune to devices with wrong clocks.
- **Writer id** is a random GUID persisted per installation (deliberately *not*
  `deviceUniqueIdentifier`, which has privacy implications).
- **CRC-32** means corruption is detected, not deserialized.

### Conflict resolution

A conflict is: local and cloud both have the slot, and they differ. One interface decides:

```csharp
public interface ISaveConflictResolver
{
    ConflictWinner Resolve(string slot, in SaveMetadata local, in SaveMetadata remote);
}
```

Built-in policies:

| Resolver | Policy | Use when                                            |
|---|---|-----------------------------------------------------|
| `LongestPlaytimeResolver` *(default)* | Most accumulated playtime wins; timestamps break ties | Progression games, recommended for most titles      |
| `LastWriteWinsResolver` | Newest timestamp wins | Saves are settings like, or playtime isn't tracked  |
| `DelegateConflictResolver` | Your lambda decides | Game specific logic (compare levels, currency, ...) |

The same resolver also handles conflicts *inside* Google Play Saved Games: the provider
opens saves with manual conflict resolution and routes Play Games' conflict callback
through your resolver so a GPGS-internal conflict and a local-vs-cloud conflict
resolve identically.

Resolvers must be deterministic. If you can't decide automatically, resolve to `Local`
and surface a choice to the player yourself using the metadata on both sides.

### Local first sync

`SaveAsync` always writes locally first, then pushes to the cloud if reachable, if
not, the slot is flagged *pending* and the upload retries on the next opportunity.
`LoadAsync` reconciles: a cloud only save is adopted locally, a pending local save is
pushed, a divergence goes through the resolver. `SyncAsync`/`SyncAllAsync` do the same
reconciliation without returning payloads, call them on app start and on focus.

Every operation reports what happened (`SaveReport`, `LoadReport`, `SyncReport`):
origin of the data, whether there was a conflict, whether the cloud was reached, and
the exact sync action taken. Nothing is silent.

### Threading

All public APIs are `async` and safe to call from the main thread. Operations on the
same slot are serialized internally; different slots may run concurrently. The
`RemoteChanged` event (iCloud only, Play Games has no push) **may fire off the main
thread**: set a flag and act on it from `Update`, or use your main thread dispatcher.

## Testing your integration

- In the **editor**, the factory wires a file backed fake cloud under
  `Library/CloudSaveKitFakeCloud`, so Play Mode exercises the real sync pipeline. Point
  two editor instances (or manual file edits) at it to simulate a second device.
- `InMemoryCloudSaveProvider` / `InMemoryLocalSaveStore` are shipped in the runtime
  assembly for your own tests: simulate outages (`IsAvailable`, `FailNextOperations`),
  remote pushes (`SimulateRemoteChange`) and corruption (`CorruptForTesting`).
- The package's own EditMode tests appear in the Test Runner when you add the package
  to `testables` in your project's `manifest.json`.

## The package's own tests

The suite is split by what CI can actually verify.

The core tests run on plain .NET, on every push and PR. Since the core has no
UnityEngine dependencies, [`.ci/CloudSaveKit.Tests.csproj`](.ci/CloudSaveKit.Tests.csproj)
compiles `Runtime/Core`, `Runtime/Providers/Local` and `Tests/Editor` into an ordinary
NUnit project:

```
dotnet test .ci/CloudSaveKit.Tests.csproj
```

That covers the envelope, the resolvers, the manager's sync logic and the file backed
local store, with no Unity license and no Editor. The project pins `LangVersion 9.0`,
the C# version Unity 2021.3 compiles, so CI rejects syntax the real target can't build.
NUnit stays on 3.x to match the API the Unity Test Framework ships, so the same test
files compile in both places.

Everything that needs UnityEngine is verified by hand in the Editor:
`CloudSaveKitFactory`, the iOS post-processor, the Google Play and iCloud providers, and
the Play Mode fake-cloud pipeline. The [Status](#status) table says which of those have
been run on a real device.


## Limits & trade-offs

- **iCloud key-value store: 1 MB total** across all keys. CloudSaveKit throws
  `PayloadTooLargeException` with the exact numbers instead of letting Apple silently
  drop the write. If your saves are bigger, compress the payload before saving; an
  iCloud Documents provider (no practical size limit, but requires more setup) is on
  the roadmap.
- **Play Games saved games: 3 MB per file** (platform limit).
- **No push notifications from Play Games:**  `RemoteChanged` only fires on Apple
  platforms. Sync on focus covers the gap in practice.
- CloudSaveKit expects to own its slots. Pointing it at cloud data written by other
  systems logs a warning and treats that data as absent.

## Status

Honest state of things, per layer, and how each one is verified:

| Layer | Status | Verified by |
|---|---|---|
| Core (envelope, resolvers, manager, local store) | ✅ Fully unit-tested | Automated, `dotnet test` in CI on every push |
| Editor fake-cloud pipeline | ✅ Tested | Manual, Play Mode in the Editor |
| `CloudSaveKitFactory` + Unity glue | ✅ Tested | Manual, Editor only, needs UnityEngine |
| Google Play provider | ⚠️ Written against GPGS v2 0.11.x, reviewed, **not yet device-verified** | Manual, Editor compile only |
| iCloud provider + Objective-C bridge | ⚠️ Written against documented APIs, reviewed, **not yet device-verified** | Manual, Editor compile only |
| Xcode post-processor | ⚠️ **Not yet verified against a real Xcode build** | Manual, Editor compile only |

Only the first row runs in CI. The rest needs someone to open the Editor.

Device verification reports (and fixes) are the most valuable contribution right now.
Issues and PRs welcome.

## License

[MIT](LICENSE)
