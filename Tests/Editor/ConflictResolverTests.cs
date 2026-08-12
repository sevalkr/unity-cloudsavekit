#nullable enable
using System;
using NUnit.Framework;

namespace SK.CloudSave.Tests
{
    public class ConflictResolverTests
    {
        private static readonly SaveMetadata OlderMorePlaytime =
            new SaveMetadata(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(10), "a");
        private static readonly SaveMetadata NewerLessPlaytime =
            new SaveMetadata(1, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(5), "b");

        [Test]
        public void LastWriteWins_PicksNewerTimestamp()
        {
            var resolver = new LastWriteWinsResolver();
            Assert.AreEqual(ConflictWinner.Remote, resolver.Resolve("s", in OlderMorePlaytime, in NewerLessPlaytime));
            Assert.AreEqual(ConflictWinner.Local, resolver.Resolve("s", in NewerLessPlaytime, in OlderMorePlaytime));
        }

        [Test]
        public void LastWriteWins_TieGoesToLocal()
        {
            var resolver = new LastWriteWinsResolver();
            Assert.AreEqual(ConflictWinner.Local, resolver.Resolve("s", in OlderMorePlaytime, in OlderMorePlaytime));
        }

        [Test]
        public void LongestPlaytime_BeatsTimestamp()
        {
            var resolver = new LongestPlaytimeResolver();
            // Remote side has more playtime despite the older timestamp.
            Assert.AreEqual(ConflictWinner.Remote, resolver.Resolve("s", in NewerLessPlaytime, in OlderMorePlaytime));
            Assert.AreEqual(ConflictWinner.Local, resolver.Resolve("s", in OlderMorePlaytime, in NewerLessPlaytime));
        }

        [Test]
        public void LongestPlaytime_TieBreaksOnTimestamp()
        {
            var a = new SaveMetadata(1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(10), "a");
            var b = new SaveMetadata(1, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(10), "b");
            var resolver = new LongestPlaytimeResolver();
            Assert.AreEqual(ConflictWinner.Remote, resolver.Resolve("s", in a, in b));
        }

        [Test]
        public void DelegateResolver_ForwardsDecision()
        {
            var resolver = new DelegateConflictResolver((string slot, in SaveMetadata l, in SaveMetadata r) => ConflictWinner.Remote);
            Assert.AreEqual(ConflictWinner.Remote, resolver.Resolve("s", in OlderMorePlaytime, in NewerLessPlaytime));
        }
    }
}
