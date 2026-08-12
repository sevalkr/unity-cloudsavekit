// iCloud key-value store provider. Compiles only in iOS builds; in the
// editor use FileCloudSaveProvider (CloudSaveKitFactory does this automatically).

#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;

namespace SK.CloudSave.Providers
{
    /// <summary>
    /// Cloud provider backed by iCloud's key-value store (NSUbiquitousKeyValueStore).
    ///
    /// Why KVS instead of Game Center saved games: it works for every user who is
    /// signed in to iCloud, no Game Center prompt, no extra account. The trade-off
    /// is Apple's hard limit of 1 MB total (and 1024 keys) per app, which this
    /// provider enforces up front with <see cref="PayloadTooLargeException"/> instead
    /// of letting saves silently vanish.
    ///
    /// iCloud KVS is last-writer-wins at the transport level: CloudSaveKit's envelope +
    /// resolver layer on top is what turns that into deterministic conflict resolution.
    /// </summary>
    public class ICloudKvsSaveProvider : ICloudSaveProvider
    {
        /// <summary>Apple's documented limit is 1 MB for the whole store; a single slot
        /// may therefore never exceed it. Kept slightly conservative.</summary>
        public const int MaxEnvelopeBytes = 1_000_000;

        private const string KeyPrefix = "csk.";

        private static ICloudKvsSaveProvider? s_instance;

        private readonly ICloudSaveLogger _log;

        public string ProviderName => "iCloud Key-Value Store";

        public bool IsAvailable => _CloudSaveKit_IsAvailable();

        /// <summary>Raised when iCloud reports that another device changed a slot.
        /// WARNING: may be raised from a non-main thread. Dispatch to the main thread
        /// before touching Unity APIs.</summary>
        public event Action<string>? RemoteChanged;

        public ICloudKvsSaveProvider(ICloudSaveLogger? logger = null)
        {
            _log = logger ?? NullCloudSaveLogger.Instance;
            s_instance = this;
        }

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            _CloudSaveKit_SetRemoteChangeCallback(OnRemoteChangeFromNative);
            bool available = IsAvailable;
            if (!available)
            {
                _log.Info("[CloudSaveKit] iCloud is not available (no iCloud account signed in). Falling back to local-only saves.");
            }
            return Task.FromResult(available);
        }

        public Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();

            IntPtr buffer = _CloudSaveKit_Load(KeyPrefix + slot, out int length);
            if (buffer == IntPtr.Zero || length <= 0)
            {
                return Task.FromResult<byte[]?>(null);
            }
            try
            {
                byte[] data = new byte[length];
                Marshal.Copy(buffer, data, 0, length);
                return Task.FromResult<byte[]?>(data);
            }
            finally
            {
                _CloudSaveKit_Free(buffer);
            }
        }

        public Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            if (data == null) throw new ArgumentNullException(nameof(data));
            ThrowIfUnavailable();

            if (data.Length > MaxEnvelopeBytes)
            {
                throw new PayloadTooLargeException(data.Length, MaxEnvelopeBytes, ProviderName);
            }

            if (!_CloudSaveKit_Save(KeyPrefix + slot, data, data.Length))
            {
                throw new CloudSaveException(
                    $"iCloud KVS rejected the write for slot '{slot}'. This usually means the 1 MB " +
                    "total quota is exhausted or the key-value store entitlement is missing.");
            }
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();
            return Task.FromResult(_CloudSaveKit_Delete(KeyPrefix + slot));
        }

        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();

            IntPtr joined = _CloudSaveKit_ListKeys(KeyPrefix);
            if (joined == IntPtr.Zero)
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
            try
            {
                string? keys = Marshal.PtrToStringUTF8(joined);
                if (string.IsNullOrEmpty(keys))
                {
                    return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
                }
                var slots = new List<string>();
                foreach (string key in keys!.Split('\n'))
                {
                    if (key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                    {
                        string slot = key.Substring(KeyPrefix.Length);
                        if (SlotName.IsValid(slot))
                        {
                            slots.Add(slot);
                        }
                    }
                }
                return Task.FromResult<IReadOnlyList<string>>(slots);
            }
            finally
            {
                _CloudSaveKit_Free(joined);
            }
        }

        private void ThrowIfUnavailable()
        {
            if (!IsAvailable)
            {
                throw new CloudSaveException("iCloud key-value store is not available: no iCloud account is signed in.");
            }
        }

        // native interop

        private delegate void RemoteChangeDelegate(string key, int changeReason);

        [MonoPInvokeCallback(typeof(RemoteChangeDelegate))]
        private static void OnRemoteChangeFromNative(string key, int changeReason)
        {
            ICloudKvsSaveProvider? instance = s_instance;
            if (instance == null || key == null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                return;
            }
            string slot = key.Substring(KeyPrefix.Length);
            if (!SlotName.IsValid(slot))
            {
                return;
            }
            // changeReason: 0=ServerChange 1=InitialSync 2=QuotaViolation 3=AccountChange
            instance._log.Info($"[CloudSaveKit] iCloud remote change for slot '{slot}' (reason {changeReason}).");
            instance.RemoteChanged?.Invoke(slot);
        }

        [DllImport("__Internal")] private static extern bool _CloudSaveKit_IsAvailable();
        [DllImport("__Internal")] private static extern bool _CloudSaveKit_Save(string key, byte[] bytes, int length);
        [DllImport("__Internal")] private static extern IntPtr _CloudSaveKit_Load(string key, out int outLength);
        [DllImport("__Internal")] private static extern bool _CloudSaveKit_Delete(string key);
        [DllImport("__Internal")] private static extern IntPtr _CloudSaveKit_ListKeys(string prefix);
        [DllImport("__Internal")] private static extern void _CloudSaveKit_Free(IntPtr pointer);
        [DllImport("__Internal")] private static extern void _CloudSaveKit_SetRemoteChangeCallback(RemoteChangeDelegate callback);
    }
}
#endif
