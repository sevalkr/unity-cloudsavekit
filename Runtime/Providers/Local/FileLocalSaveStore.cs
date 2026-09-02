#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace SK.CloudSave.Providers
{
    /// <summary>
    /// File-backed <see cref="ILocalSaveStore"/> with atomic writes: data is written
    /// to a temp file and moved into place, so a crash mid-write can never leave a
    /// half-written save behind. Pending-upload flags are marker files next to the saves.
    /// </summary>
    public class FileLocalSaveStore : ILocalSaveStore
    {
        private const string SaveExtension = ".save";
        private const string PendingExtension = ".pending";
        private const string TempExtension = ".tmp";

        private readonly string _rootDirectory;

        public FileLocalSaveStore(string rootDirectory)
        {
            _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            Directory.CreateDirectory(_rootDirectory);
        }

        public byte[]? Load(string slot)
        {
            SlotName.ThrowIfInvalid(slot);
            string path = SavePath(slot);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public void Save(string slot, byte[] data)
        {
            SlotName.ThrowIfInvalid(slot);
            if (data == null) throw new ArgumentNullException(nameof(data));

            Directory.CreateDirectory(_rootDirectory);
            string finalPath = SavePath(slot);
            string tempPath = finalPath + TempExtension;

            File.WriteAllBytes(tempPath, data);

            // Replace is atomic but needs the destination to exist, so a first save moves.
            if (File.Exists(finalPath))
            {
                File.Replace(tempPath, finalPath, null);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }

        public bool Delete(string slot)
        {
            SlotName.ThrowIfInvalid(slot);
            string path = SavePath(slot);
            bool existed = File.Exists(path);
            if (existed)
            {
                File.Delete(path);
            }
            string pendingPath = PendingPath(slot);
            if (File.Exists(pendingPath))
            {
                File.Delete(pendingPath);
            }
            return existed;
        }

        public IReadOnlyList<string> ListSlots()
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return Array.Empty<string>();
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
            return slots;
        }

        public bool GetPendingUpload(string slot)
        {
            SlotName.ThrowIfInvalid(slot);
            return File.Exists(PendingPath(slot));
        }

        public void SetPendingUpload(string slot, bool pending)
        {
            SlotName.ThrowIfInvalid(slot);
            string path = PendingPath(slot);
            if (pending)
            {
                Directory.CreateDirectory(_rootDirectory);
                if (!File.Exists(path))
                {
                    File.WriteAllBytes(path, Array.Empty<byte>());
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string SavePath(string slot) => Path.Combine(_rootDirectory, slot + SaveExtension);
        private string PendingPath(string slot) => Path.Combine(_rootDirectory, slot + PendingExtension);
    }
}
