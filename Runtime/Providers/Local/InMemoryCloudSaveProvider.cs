#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SK.CloudSave.Providers
{
    /// <summary>
    /// In-memory cloud provider for unit tests. Supports simulating outages
    /// (<see cref="IsAvailable"/> / <see cref="FailNextOperations"/>) and remote
    /// pushes (<see cref="SimulateRemoteChange"/>).
    /// </summary>
    public class InMemoryCloudSaveProvider : ICloudSaveProvider
    {
        private readonly Dictionary<string, byte[]> _storage = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public string ProviderName => "InMemory (test)";
        public bool IsAvailable { get; set; } = true;

        /// <summary>While &gt; 0, each Load/Save/Delete/List throws and decrements the counter.</summary>
        public int FailNextOperations { get; set; }

        public int SaveCallCount { get; private set; }

        public event Action<string>? RemoteChanged;

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default) => Task.FromResult(IsAvailable);

        public Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            return Task.FromResult(_storage.TryGetValue(slot, out byte[]? data) ? (byte[]?)data.ToArray() : null);
        }

        public Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            SaveCallCount++;
            _storage[slot] = data.ToArray();
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            return Task.FromResult(_storage.Remove(slot));
        }

        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFailing();
            return Task.FromResult<IReadOnlyList<string>>(_storage.Keys.ToList());
        }

        /// <summary>Writes data directly into the fake cloud (as another device would)
        /// and raises <see cref="RemoteChanged"/>.</summary>
        public void SimulateRemoteChange(string slot, byte[] data)
        {
            _storage[slot] = data.ToArray();
            RemoteChanged?.Invoke(slot);
        }

        public byte[]? PeekStored(string slot) => _storage.TryGetValue(slot, out byte[]? data) ? data.ToArray() : null;

        private void ThrowIfFailing()
        {
            if (FailNextOperations > 0)
            {
                FailNextOperations--;
                throw new CloudSaveException("Simulated cloud failure.");
            }
        }
    }

    /// <summary>In-memory <see cref="ILocalSaveStore"/> for unit tests.</summary>
    public class InMemoryLocalSaveStore : ILocalSaveStore
    {
        private readonly Dictionary<string, byte[]> _storage = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);

        public byte[]? Load(string slot) => _storage.TryGetValue(slot, out byte[]? data) ? data.ToArray() : null;
        public void Save(string slot, byte[] data) => _storage[slot] = data.ToArray();
        public bool Delete(string slot)
        {
            _pending.Remove(slot);
            return _storage.Remove(slot);
        }
        public IReadOnlyList<string> ListSlots() => _storage.Keys.ToList();
        public bool GetPendingUpload(string slot) => _pending.Contains(slot);
        public void SetPendingUpload(string slot, bool pending)
        {
            if (pending) _pending.Add(slot);
            else _pending.Remove(slot);
        }

        /// <summary>Corrupts a stored envelope in place, for corruption handling tests.</summary>
        public void CorruptForTesting(string slot, int byteIndex)
        {
            byte[] data = _storage[slot];
            data[byteIndex] ^= 0xFF;
        }
    }
}
