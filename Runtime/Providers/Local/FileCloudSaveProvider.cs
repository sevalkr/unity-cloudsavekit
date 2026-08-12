#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SK.CloudSave.Providers
{
    /// <summary>
    /// A "cloud" backed by a directory on disk. Intended for the Unity Editor and for
    /// development builds: it lets you exercise the full sync/conflict pipeline
    /// (including simulating a second device by pointing another instance at the same
    /// directory) without any platform services.
    /// </summary>
    public class FileCloudSaveProvider : ICloudSaveProvider
    {
        private const string SaveExtension = ".cloudsave";

        private readonly string _rootDirectory;

        public string ProviderName => "FileCloud (dev)";
        public bool IsAvailable { get; private set; } = true;

#pragma warning disable CS0067
        public event Action<string>? RemoteChanged;
#pragma warning restore CS0067

        public FileCloudSaveProvider(string rootDirectory)
        {
            _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        }

        /// <summary>Simulate the cloud going offline/online in tests and play mode experiments.</summary>
        public void SetAvailableForTesting(bool available) => IsAvailable = available;

        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_rootDirectory);
            return Task.FromResult(IsAvailable);
        }

        public Task<byte[]?> LoadAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();
            string path = SavePath(slot);
            return Task.FromResult(File.Exists(path) ? File.ReadAllBytes(path) : (byte[]?)null);
        }

        public Task SaveAsync(string slot, byte[] data, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            if (data == null) throw new ArgumentNullException(nameof(data));
            ThrowIfUnavailable();
            Directory.CreateDirectory(_rootDirectory);
            string finalPath = SavePath(slot);
            string tempPath = finalPath + ".tmp";
            File.WriteAllBytes(tempPath, data);
#if UNITY_2021_2_OR_NEWER || NETSTANDARD2_1 || NET5_0_OR_GREATER
            File.Move(tempPath, finalPath);
#else
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            File.Move(tempPath, finalPath);
#endif
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string slot, CancellationToken cancellationToken = default)
        {
            SlotName.ThrowIfInvalid(slot);
            ThrowIfUnavailable();
            string path = SavePath(slot);
            bool existed = File.Exists(path);
            if (existed)
            {
                File.Delete(path);
            }
            return Task.FromResult(existed);
        }

        public Task<IReadOnlyList<string>> ListSlotsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfUnavailable();
            if (!Directory.Exists(_rootDirectory))
            {
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
            var slots = new List<string>();
            foreach (string file in Directory.GetFiles(_rootDirectory, "*" + SaveExtension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (SlotName.IsValid(name))
                {
                    slots.Add(name);
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(slots);
        }

        private void ThrowIfUnavailable()
        {
            if (!IsAvailable)
            {
                throw new CloudSaveException($"{ProviderName} is currently unavailable (simulated offline).");
            }
        }

        private string SavePath(string slot) => Path.Combine(_rootDirectory, slot + SaveExtension);
    }
}
