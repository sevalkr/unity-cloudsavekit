#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SK.CloudSave
{
    public class CloudSaveOptions
    {
        /// <summary>Version of your game's save schema, stamped into every envelope.
        /// Bump it when your save format changes so future versions of your game can migrate.</summary>
        public int SchemaVersion { get; set; } = 1;
    }

    /// <summary>
    /// Local first save orchestration on top of an <see cref="ILocalSaveStore"/> and an
    /// <see cref="ICloudSaveProvider"/>.
    /// </summary>
    public class CloudSaveManager : IDisposable
    {
        private readonly ICloudSaveProvider _cloud;
        private readonly ILocalSaveStore _local;
        private readonly ISaveConflictResolver _resolver;
        private readonly ICloudSaveLogger _log;
        private readonly CloudSaveOptions _options;
        private readonly string _writerId;
        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<string, SemaphoreSlim> _slotLocks = new Dictionary<string, SemaphoreSlim>();
        
        public event Action<string>? RemoteChanged;

        public ICloudSaveProvider CloudProvider => _cloud;

        public CloudSaveManager(
            ICloudSaveProvider cloudProvider,
            ILocalSaveStore localStore,
            ISaveConflictResolver conflictResolver,
            string writerId,
            CloudSaveOptions? options = null,
            ICloudSaveLogger? logger = null,
            Func<DateTime>? utcNow = null)
        {
            _cloud = cloudProvider ?? throw new ArgumentNullException(nameof(cloudProvider));
            _local = localStore ?? throw new ArgumentNullException(nameof(localStore));
            _resolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
            _writerId = writerId ?? throw new ArgumentNullException(nameof(writerId));
            _options = options ?? new CloudSaveOptions();
            _log = logger ?? NullCloudSaveLogger.Instance;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);

            _cloud.RemoteChanged += OnProviderRemoteChanged;
        }

        private void OnProviderRemoteChanged(string slot) => RemoteChanged?.Invoke(slot);

        /// <summary>Initializes the cloud provider (sign-in, capability checks, ...).
        /// Returns whether the cloud is usable. The manager works fine either way,
        /// without cloud it simply behaves as a robust local save system.</summary>
        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default) =>
            _cloud.InitializeAsync(cancellationToken);

        /// <summary>
        /// Persists <paramref name="payload"/> locally and pushes it to the cloud when reachable.
        /// <paramref name="totalPlaytime"/> should be the total accumulated play time of this
        /// save file, it is the primary signal playtime-based conflict resolvers use.
        /// </summary>
        public async Task<SaveReport> SaveAsync(string slot, byte[] payload, TimeSpan totalPlaytime, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            SemaphoreSlim slotLock = GetSlotLock(slot);
            await slotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var metadata = new SaveMetadata(_options.SchemaVersion, _utcNow(), totalPlaytime, _writerId);
                byte[] envelope = SaveEnvelope.Write(in metadata, payload);

                _local.Save(slot, envelope);
                _local.SetPendingUpload(slot, true);

                if (!_cloud.IsAvailable)
                {
                    return new SaveReport(savedLocally: true, pushedToCloud: false, cloudError: null, metadata);
                }

                try
                {
                    await _cloud.SaveAsync(slot, envelope, cancellationToken).ConfigureAwait(false);
                    _local.SetPendingUpload(slot, false);
                    return new SaveReport(savedLocally: true, pushedToCloud: true, cloudError: null, metadata);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _log.Warn($"[CloudSaveKit] Cloud push failed for slot '{slot}', kept as pending: {e.Message}");
                    return new SaveReport(savedLocally: true, pushedToCloud: false, cloudError: e, metadata);
                }
            }
            finally
            {
                slotLock.Release();
            }
        }

        /// <summary>
        /// Loads the winning version of a slot, reconciling local and cloud along the way:
        /// a newer cloud save replaces the local copy, a pending local save is uploaded.
        /// </summary>
        public async Task<LoadReport> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);

            SemaphoreSlim slotLock = GetSlotLock(slot);
            await slotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ReconcileAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                slotLock.Release();
            }
        }

        /// <summary>
        /// Reconciles a slot between local and cloud without returning the payload.
        /// Call it on app start, on app resume, or in response to <see cref="RemoteChanged"/>.
        /// </summary>
        public async Task<SyncReport> SyncAsync(string slot, CancellationToken cancellationToken = default)
        {
            LoadReport report = await LoadAsync(slot, cancellationToken).ConfigureAwait(false);
            return new SyncReport(report.SyncAction, report.HadConflict);
        }

        /// <summary>Reconciles every slot known either locally or in the cloud.</summary>
        public async Task<IReadOnlyDictionary<string, SyncReport>> SyncAllAsync(CancellationToken cancellationToken = default)
        {
            var slots = new HashSet<string>(_local.ListSlots(), StringComparer.Ordinal);
            if (_cloud.IsAvailable)
            {
                try
                {
                    foreach (string slot in await _cloud.ListSlotsAsync(cancellationToken).ConfigureAwait(false))
                    {
                        slots.Add(slot);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _log.Warn($"[CloudSaveKit] Listing cloud slots failed, syncing local slots only: {e.Message}");
                }
            }

            var reports = new Dictionary<string, SyncReport>(slots.Count, StringComparer.Ordinal);
            foreach (string slot in slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reports[slot] = await SyncAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            return reports;
        }

        /// <summary>Deletes a slot locally and, when <paramref name="alsoCloud"/> is true
        /// and the cloud is reachable, remotely as well. Returns whether anything was deleted.</summary>
        public async Task<bool> DeleteAsync(string slot, bool alsoCloud = true, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);

            SemaphoreSlim slotLock = GetSlotLock(slot);
            await slotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                bool deletedLocal = _local.Delete(slot);
                _local.SetPendingUpload(slot, false);

                bool deletedCloud = false;
                if (alsoCloud && _cloud.IsAvailable)
                {
                    try
                    {
                        deletedCloud = await _cloud.DeleteAsync(slot, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        _log.Warn($"[CloudSaveKit] Cloud delete failed for slot '{slot}': {e.Message}");
                    }
                }
                return deletedLocal || deletedCloud;
            }
            finally
            {
                slotLock.Release();
            }
        }

        private async Task<LoadReport> ReconcileAsync(string slot, CancellationToken cancellationToken)
        {
            //  Gather local side
            byte[]? localEnvelope = _local.Load(slot);
            SaveMetadata localMeta = default;
            byte[]? localPayload = null;
            if (localEnvelope != null)
            {
                EnvelopeReadResult result = SaveEnvelope.TryRead(localEnvelope, out localMeta, out byte[] payload);
                if (result == EnvelopeReadResult.Ok)
                {
                    localPayload = payload;
                }
                else
                {
                    _log.Error($"[CloudSaveKit] Local save for slot '{slot}' is unreadable ({result}); treating as absent.");
                    localEnvelope = null;
                }
            }

            //  Gather cloud side
            bool cloudReachable = _cloud.IsAvailable;
            byte[]? cloudEnvelope = null;
            SaveMetadata cloudMeta = default;
            byte[]? cloudPayload = null;
            if (cloudReachable)
            {
                try
                {
                    cloudEnvelope = await _cloud.LoadAsync(slot, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _log.Warn($"[CloudSaveKit] Cloud load failed for slot '{slot}': {e.Message}");
                    cloudReachable = false;
                }

                if (cloudEnvelope != null)
                {
                    EnvelopeReadResult result = SaveEnvelope.TryRead(cloudEnvelope, out cloudMeta, out byte[] payload);
                    if (result == EnvelopeReadResult.Ok)
                    {
                        cloudPayload = payload;
                    }
                    else
                    {
                        _log.Error($"[CloudSaveKit] Cloud save for slot '{slot}' is unreadable ({result}); treating as absent.");
                        cloudEnvelope = null;
                    }
                }
            }

            bool hasLocal = localPayload != null;
            bool hasCloud = cloudPayload != null;

            //  Neither side has data
            if (!hasLocal && !hasCloud)
            {
                SyncAction action = cloudReachable ? SyncAction.None : SyncAction.CloudUnavailable;
                return new LoadReport(SaveOrigin.None, null, default, hadConflict: false, action);
            }

            // Cloud only: adopt it locally
            if (!hasLocal)
            {
                _local.Save(slot, cloudEnvelope!);
                _local.SetPendingUpload(slot, false);
                return new LoadReport(SaveOrigin.Cloud, cloudPayload, cloudMeta, hadConflict: false, SyncAction.PulledCloudToLocal);
            }

            //Local only
            if (!hasCloud)
            {
                SyncAction action = SyncAction.None;
                if (!cloudReachable)
                {
                    action = SyncAction.CloudUnavailable;
                }
                else if (_local.GetPendingUpload(slot))
                {
                    action = await TryPushAsync(slot, localEnvelope!, cancellationToken).ConfigureAwait(false)
                        ? SyncAction.PushedLocalToCloud
                        : SyncAction.CloudUnavailable;
                }
                return new LoadReport(SaveOrigin.Local, localPayload, localMeta, hadConflict: false, action);
            }

            // Both sides exist
            if (localMeta.Equals(cloudMeta))
            {
                // Same save on both sides; nothing pending by definition.
                _local.SetPendingUpload(slot, false);
                return new LoadReport(SaveOrigin.Local, localPayload, localMeta, hadConflict: false, SyncAction.None);
            }

            ConflictWinner winner = _resolver.Resolve(slot, in localMeta, in cloudMeta);
            _log.Info($"[CloudSaveKit] Conflict on slot '{slot}': local({localMeta.TimestampUtc:O}, {localMeta.TotalPlaytime}) vs cloud({cloudMeta.TimestampUtc:O}, {cloudMeta.TotalPlaytime}) -> {winner}");

            if (winner == ConflictWinner.Remote)
            {
                _local.Save(slot, cloudEnvelope!);
                _local.SetPendingUpload(slot, false);
                return new LoadReport(SaveOrigin.Cloud, cloudPayload, cloudMeta, hadConflict: true, SyncAction.PulledCloudToLocal);
            }
            else
            {
                bool pushed = await TryPushAsync(slot, localEnvelope!, cancellationToken).ConfigureAwait(false);
                return new LoadReport(SaveOrigin.Local, localPayload, localMeta, hadConflict: true,
                    pushed ? SyncAction.PushedLocalToCloud : SyncAction.CloudUnavailable);
            }
        }

        private async Task<bool> TryPushAsync(string slot, byte[] envelope, CancellationToken cancellationToken)
        {
            try
            {
                await _cloud.SaveAsync(slot, envelope, cancellationToken).ConfigureAwait(false);
                _local.SetPendingUpload(slot, false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _local.SetPendingUpload(slot, true);
                _log.Warn($"[CloudSaveKit] Cloud push failed for slot '{slot}', kept as pending: {e.Message}");
                return false;
            }
        }

        private SemaphoreSlim GetSlotLock(string slot)
        {
            lock (_slotLocks)
            {
                if (!_slotLocks.TryGetValue(slot, out SemaphoreSlim? slotLock))
                {
                    slotLock = new SemaphoreSlim(1, 1);
                    _slotLocks[slot] = slotLock;
                }
                return slotLock;
            }
        }

        public void Dispose()
        {
            _cloud.RemoteChanged -= OnProviderRemoteChanged;
            lock (_slotLocks)
            {
                foreach (SemaphoreSlim slotLock in _slotLocks.Values)
                {
                    slotLock.Dispose();
                }
                _slotLocks.Clear();
            }
        }
    }
}
