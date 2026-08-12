#nullable enable
using System;

namespace SK.CloudSave
{
    public enum SaveOrigin
    {
        None,
        Local,
        Cloud,
    }

    public enum SyncAction
    {
        /// <summary>Local and cloud were already in agreement (or only one side exists and nothing was pending).</summary>
        None,
        /// <summary>Local version won or was pending; it was uploaded to the cloud.</summary>
        PushedLocalToCloud,
        /// <summary>Cloud version won; it replaced the local copy.</summary>
        PulledCloudToLocal,
        /// <summary>Cloud was unreachable or unavailable; only local state was touched.</summary>
        CloudUnavailable,
    }

    /// <summary>Result of <see cref="CloudSaveManager.SaveAsync"/>.</summary>
    public readonly struct SaveReport
    {
        public bool SavedLocally { get; }
        
        public bool PushedToCloud { get; }

        public Exception? CloudError { get; }

        public SaveMetadata Metadata { get; }

        public SaveReport(bool savedLocally, bool pushedToCloud, Exception? cloudError, SaveMetadata metadata)
        {
            SavedLocally = savedLocally;
            PushedToCloud = pushedToCloud;
            CloudError = cloudError;
            Metadata = metadata;
        }
    }

    /// <summary>Result of <see cref="CloudSaveManager.LoadAsync"/>.</summary>
    public readonly struct LoadReport
    {
        public bool Found => Origin != SaveOrigin.None;
        public SaveOrigin Origin { get; }
        public byte[]? Payload { get; }
        public SaveMetadata Metadata { get; }
        
        public bool HadConflict { get; }

        public SyncAction SyncAction { get; }

        public LoadReport(SaveOrigin origin, byte[]? payload, SaveMetadata metadata, bool hadConflict, SyncAction syncAction)
        {
            Origin = origin;
            Payload = payload;
            Metadata = metadata;
            HadConflict = hadConflict;
            SyncAction = syncAction;
        }
    }

    /// <summary>Result of <see cref="CloudSaveManager.SyncAsync"/>.</summary>
    public readonly struct SyncReport
    {
        public SyncAction Action { get; }
        public bool HadConflict { get; }

        public SyncReport(SyncAction action, bool hadConflict)
        {
            Action = action;
            HadConflict = hadConflict;
        }
    }
}
