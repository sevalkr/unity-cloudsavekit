// Compiled only when the Google Play Games plugin v2 is present.
// - UPM install (com.google.play.games): the CLOUDSAVEKIT_GPGS define is set automatically
//   via this package's asmdef versionDefines.
// - .unitypackage install: add CLOUDSAVEKIT_GPGS to Project Settings > Player > Scripting Define Symbols.
//

#if CLOUDSAVEKIT_GPGS && (UNITY_ANDROID || UNITY_EDITOR)
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;

namespace SK.CloudSave.Providers
{
    /// <summary>
    /// Cloud provider backed by Google Play Games Services "Saved Games".
    ///
    /// Design notes:
    /// <list type="bullet">
    /// <item>Saved games are opened with <b>manual</b> conflict resolution: when Play Games
    /// reports two conflicting versions of the same file (e.g. two devices synced offline
    /// progress), both envelopes are parsed and the same <see cref="ISaveConflictResolver"/>
    /// used everywhere else decides, so conflict behavior is identical across the whole stack.</item>
    /// <item>No cross-call metadata caching: existence checks always fetch a fresh list.
    /// A stale cache here would make saves created on another device invisible until restart.</item>
    /// </list>
    /// </summary>
    public class GooglePlaySaveProvider : ICloudSaveProvider
    {
        private readonly ISaveConflictResolver _resolver;
        private readonly ICloudSaveLogger _log;

        public string ProviderName => "Google Play Saved Games";

        public bool IsAvailable =>
            PlayGamesPlatform.Instance != null
            && PlayGamesPlatform.Instance.IsAuthenticated()
            && PlayGamesPlatform.Instance.SavedGame != null;

#pragma warning disable CS0067
        /// <summary>Play Games does not push remote change notifications; this event never fires.
        /// Call <see cref="CloudSaveManager.SyncAsync"/> on app focus instead.</summary>
        public event Action<string>? RemoteChanged;
#pragma warning restore CS0067

        public GooglePlaySaveProvider(ISaveConflictResolver conflictResolver, ICloudSaveLogger? logger = null)
        {
            _resolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
            _log = logger ?? NullCloudSaveLogger.Instance;
        }

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                PlayGamesPlatform.Instance.Authenticate(status =>
                {
                    if (status != SignInStatus.Success)
                    {
                        _log.Info(
                            $"[CloudSaveKit] Play Games sign-in unavailable: {status}. Falling back to local-only saves.");
                    }

                    tcs.TrySetResult(status == SignInStatus.Success);
                });
                return tcs.Task;
            }
        }

        public async Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();

            // Opening a saved game creates it when missing, so existence must be
            // checked first, with a fresh fetch, never a cached one.
            if (!await ExistsAsync(slot, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            ISavedGameMetadata metadata = await OpenAsync(slot, cancellationToken).ConfigureAwait(false);

            var tcs = new TaskCompletionSource<byte[]?>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(metadata, (status, data) =>
                {
                    if (status == SavedGameRequestStatus.Success)
                    {
                        // A just created or never written file reads as empty.
                        tcs.TrySetResult(data != null && data.Length > 0 ? data : null);
                    }
                    else
                    {
                        tcs.TrySetException(
                            new CloudSaveException($"Play Games read failed for slot '{slot}': {status}"));
                    }
                });
                return await tcs.Task.ConfigureAwait(false);
            }
        }

        public async Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            ThrowIfUnavailable();

            ISavedGameMetadata metadata = await OpenAsync(slot, cancellationToken).ConfigureAwait(false);

            var updateBuilder = new SavedGameMetadataUpdate.Builder();
            if (SaveEnvelope.TryReadMetadata(data, out SaveMetadata envelopeMeta) == EnvelopeReadResult.Ok)
            {
                // Mirror envelope metadata into Play Games metadata so the saves also
                // look right in the Play Games UI and to native tooling.
                updateBuilder = updateBuilder
                    .WithUpdatedPlayedTime(envelopeMeta.TotalPlaytime)
                    .WithUpdatedDescription($"Saved {envelopeMeta.TimestampUtc:u}");
            }

            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                PlayGamesPlatform.Instance.SavedGame.CommitUpdate(metadata, updateBuilder.Build(), data, (status, _) =>
                {
                    if (status == SavedGameRequestStatus.Success)
                    {
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        tcs.TrySetException(
                            new CloudSaveException($"Play Games commit failed for slot '{slot}': {status}"));
                    }
                });
                await tcs.Task.ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();

            ISavedGameMetadata? existing = await FindFreshMetadataAsync(slot, cancellationToken).ConfigureAwait(false);
            if (existing == null)
            {
                return false;
            }

            PlayGamesPlatform.Instance.SavedGame.Delete(existing);
            return true;
        }

        public async Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            List<ISavedGameMetadata> all = await FetchAllAsync(cancellationToken).ConfigureAwait(false);
            return all.Select(m => m.Filename).Where(SlotName.IsValid).ToList();
        }

        //internals

        private async Task<bool> ExistsAsync(string slot, CancellationToken cancellationToken) =>
            await FindFreshMetadataAsync(slot, cancellationToken).ConfigureAwait(false) != null;

        private async Task<ISavedGameMetadata?> FindFreshMetadataAsync(string slot, CancellationToken cancellationToken)
        {
            List<ISavedGameMetadata> all = await FetchAllAsync(cancellationToken).ConfigureAwait(false);
            return all.FirstOrDefault(m => string.Equals(m.Filename, slot, StringComparison.Ordinal));
        }

        private Task<List<ISavedGameMetadata>> FetchAllAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<List<ISavedGameMetadata>>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                PlayGamesPlatform.Instance.SavedGame.FetchAllSavedGames(DataSource.ReadCacheOrNetwork,
                    (status, metadataList) =>
                    {
                        if (status == SavedGameRequestStatus.Success && metadataList != null)
                        {
                            tcs.TrySetResult(metadataList);
                        }
                        else
                        {
                            tcs.TrySetException(new CloudSaveException($"Play Games fetch failed: {status}"));
                        }
                    });
                return tcs.Task;
            }
        }

        private Task<ISavedGameMetadata> OpenAsync(string slot, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<ISavedGameMetadata>();
            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                PlayGamesPlatform.Instance.SavedGame.OpenWithManualConflictResolution(
                    slot,
                    DataSource.ReadCacheOrNetwork,
                    prefetchDataOnConflict: true,
                    conflictCallback: ResolveOpenConflict,
                    completedCallback: (status, metadata) =>
                    {
                        if (status == SavedGameRequestStatus.Success && metadata != null)
                        {
                            tcs.TrySetResult(metadata);
                        }
                        else
                        {
                            tcs.TrySetException(
                                new CloudSaveException($"Play Games open failed for slot '{slot}': {status}"));
                        }
                    });
                return tcs.Task;
            }
        }

        /// <summary>
        /// Called by Play Games when the same saved game has two divergent versions.
        /// Both blobs are CloudSaveKit envelopes, so the shared resolver decides.
        /// Falls back to the more recently modified version when either blob is unreadable.
        /// </summary>
        private void ResolveOpenConflict(
            IConflictResolver resolver,
            ISavedGameMetadata original,
            byte[] originalData,
            ISavedGameMetadata unmerged,
            byte[] unmergedData)
        {
            SaveMetadata originalMeta = default;
            SaveMetadata unmergedMeta = default;
            bool originalOk = originalData != null
                              && SaveEnvelope.TryReadMetadata(originalData, out originalMeta) == EnvelopeReadResult.Ok;
            bool unmergedOk = unmergedData != null
                              && SaveEnvelope.TryReadMetadata(unmergedData, out unmergedMeta) == EnvelopeReadResult.Ok;

            ISavedGameMetadata winner;
            if (originalOk && unmergedOk)
            {
                ConflictWinner choice = _resolver.Resolve(original.Filename, in originalMeta, in unmergedMeta);
                winner = choice == ConflictWinner.Local ? original : unmerged;
                _log.Info(
                    $"[CloudSaveKit] Play Games conflict on '{original.Filename}' resolved by {_resolver.GetType().Name}: {choice}");
            }
            else if (originalOk)
            {
                winner = original;
                _log.Warn(
                    $"[CloudSaveKit] Play Games conflict on '{original.Filename}': one side unreadable, keeping the readable one.");
            }
            else if (unmergedOk)
            {
                winner = unmerged;
                _log.Warn(
                    $"[CloudSaveKit] Play Games conflict on '{original.Filename}': one side unreadable, keeping the readable one.");
            }
            else
            {
                // Neither parses as a CloudSaveKit envelope (foreign data?). Fall back to recency.
                winner = unmerged.LastModifiedTimestamp > original.LastModifiedTimestamp ? unmerged : original;
                _log.Warn(
                    $"[CloudSaveKit] Play Games conflict on '{original.Filename}': neither side is a CloudSaveKit envelope, keeping most recent.");
            }

            resolver.ChooseMetadata(winner);
        }

        private void ThrowIfUnavailable()
        {
            if (!IsAvailable)
            {
                throw new CloudSaveException(
                    "Google Play Saved Games is not available: user is not signed in to Play Games.");
            }
        }
    }
}
#endif