#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SK.CloudSave
{
    public interface ICloudSaveProvider
    {
        string ProviderName { get; }
        
        bool IsAvailable { get; }
        
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

        Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default);

        Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default);

        /// <summary>Raised when the backend reports that a slot changed remotely
        /// (currently only iCloud KVS pushes such notifications). The argument is the
        /// slot name. May be raised from a non main thread.</summary>
        event Action<string>? RemoteChanged;
    }

    /// <summary>A provider that is never available. Used on unsupported platforms so
    /// that game code can stay branch free: everything degrades to local only saves.</summary>
    public sealed class NullCloudSaveProvider : ICloudSaveProvider
    {
        public string ProviderName => "None";
        public bool IsAvailable => false;
#pragma warning disable CS0067
        public event Action<string>? RemoteChanged;
#pragma warning restore CS0067

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default) => throw new CloudSaveException("NullCloudSaveProvider cannot save.");
        public Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
