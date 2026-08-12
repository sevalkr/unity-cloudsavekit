#nullable enable
using System;

namespace SK.CloudSave
{
    public enum ConflictWinner
    {
        Local,
        Remote,
    }

    /// <summary>
    /// Decides which version of a save wins when the local device and the cloud
    /// disagree. Implementations must be deterministic: given the same two metadata
    /// values they must always return the same winner, on every platform. That is
    /// what makes sync behavior predictable for players.
    /// </summary>
    public interface ISaveConflictResolver
    {
        ConflictWinner Resolve(string slot, in SaveMetadata local, in SaveMetadata remote);
    }

    /// <summary>The save with the most recent timestamp wins. Ties go to local
    /// (no download needed). Simple, but vulnerable to devices with wrong clocks.</summary>
    public sealed class LastWriteWinsResolver : ISaveConflictResolver
    {
        public ConflictWinner Resolve(string slot, in SaveMetadata local, in SaveMetadata remote) =>
            remote.TimestampUtc > local.TimestampUtc ? ConflictWinner.Remote : ConflictWinner.Local;
    }

    /// <summary>
    /// The save with the most accumulated play time wins; timestamps only break ties.
    /// This is the recommended default for progression games: play time never goes
    /// backwards, so it is immune to devices with misconfigured clocks, and it maps
    /// to what players actually mean by "my latest progress".
    /// </summary>
    public sealed class LongestPlaytimeResolver : ISaveConflictResolver
    {
        public ConflictWinner Resolve(string slot, in SaveMetadata local, in SaveMetadata remote)
        {
            if (remote.TotalPlaytime != local.TotalPlaytime)
            {
                return remote.TotalPlaytime > local.TotalPlaytime ? ConflictWinner.Remote : ConflictWinner.Local;
            }
            return remote.TimestampUtc > local.TimestampUtc ? ConflictWinner.Remote : ConflictWinner.Local;
        }
    }

    /// <summary>Adapts a delegate into a resolver, for game specific policies.</summary>
    public sealed class DelegateConflictResolver : ISaveConflictResolver
    {
        public delegate ConflictWinner ResolveDelegate(string slot, in SaveMetadata local, in SaveMetadata remote);

        private readonly ResolveDelegate _resolve;

        public DelegateConflictResolver(ResolveDelegate resolve)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        public ConflictWinner Resolve(string slot, in SaveMetadata local, in SaveMetadata remote) =>
            _resolve(slot, in local, in remote);
    }
}
