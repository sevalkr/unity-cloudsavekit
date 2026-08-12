#nullable enable
using System;

namespace SK.CloudSave
{
    /// <summary>
    /// Metadata carried alongside every save payload. Used by conflict resolvers
    /// to decide which version of a save wins when two devices disagree.
    /// </summary>
    public readonly struct SaveMetadata : IEquatable<SaveMetadata>
    {
        /// <summary>Version of the game's own save schema. Bump when your save format changes.</summary>
        public int SchemaVersion { get; }

        /// <summary>UTC time when this save was written.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Total accumulated play time reported by the game.</summary>
        public TimeSpan TotalPlaytime { get; }

        /// <summary>Stable identifier of the installation that wrote this save.</summary>
        public string WriterId { get; }

        public SaveMetadata(int schemaVersion, DateTime timestampUtc, TimeSpan totalPlaytime, string writerId)
        {
            if (timestampUtc.Kind != DateTimeKind.Utc)
            {
                timestampUtc = timestampUtc.ToUniversalTime();
            }
            SchemaVersion = schemaVersion;
            TimestampUtc = timestampUtc;
            TotalPlaytime = totalPlaytime;
            WriterId = writerId ?? string.Empty;
        }

        public bool Equals(SaveMetadata other) =>
            SchemaVersion == other.SchemaVersion
            && TimestampUtc == other.TimestampUtc
            && TotalPlaytime == other.TotalPlaytime
            && string.Equals(WriterId, other.WriterId, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SaveMetadata other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SchemaVersion, TimestampUtc, TotalPlaytime, WriterId);

        public override string ToString() =>
            $"SaveMetadata(schema={SchemaVersion}, utc={TimestampUtc:O}, playtime={TotalPlaytime}, writer={WriterId})";
    }
}
