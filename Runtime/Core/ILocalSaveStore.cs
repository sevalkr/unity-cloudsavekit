#nullable enable
using System.Collections.Generic;

namespace SK.CloudSave
{
    /// <summary>
    /// Synchronous local persistence used as the source of truth on the device.
    /// CloudSaveKit is local first: every save lands here immediately; the cloud is
    /// an eventually consistent replica. The store also tracks which slots have
    /// local changes that have not reached the cloud yet ("pending upload").
    /// </summary>
    public interface ILocalSaveStore
    {
        byte[]? Load(string slot);

        void Save(string slot, byte[] data);

        bool Delete(string slot);

        IReadOnlyList<string> ListSlots();

        bool GetPendingUpload(string slot);

        void SetPendingUpload(string slot, bool pending);
    }
}
