#nullable enable
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SK.CloudSave.Providers;
using NUnit.Framework;

namespace SK.CloudSave.Tests
{
    public class CloudSaveManagerTests
    {
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"gold\":1200,\"sheep\":7}");
        private static readonly DateTime Time = new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc);

        private InMemoryCloudSaveProvider _cloud = null!;
        private InMemoryLocalSaveStore _local = null!;
        private CloudSaveManager _manager = null!;

        [SetUp]
        public void SetUp()
        {
            _cloud = new InMemoryCloudSaveProvider();
            _local = new InMemoryLocalSaveStore();
            _manager = new CloudSaveManager(
                _cloud, _local, new LongestPlaytimeResolver(), "writer-1",
                new CloudSaveOptions { SchemaVersion = 2 }, null, () => Time);
        }

        [TearDown]
        public void TearDown() => _manager.Dispose();

        private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

        [Test]
        public void Save_WithCloudAvailable_PushesAndClearsPending()
        {
            SaveReport report = Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));

            Assert.IsTrue(report.SavedLocally);
            Assert.IsTrue(report.PushedToCloud);
            Assert.IsNull(report.CloudError);
            Assert.IsFalse(_local.GetPendingUpload("main"));
            Assert.IsNotNull(_cloud.PeekStored("main"));
        }

        [Test]
        public void Save_StampsSchemaVersionAndWriterId()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));
            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.AreEqual(2, load.Metadata.SchemaVersion);
            Assert.AreEqual("writer-1", load.Metadata.WriterId);
            Assert.AreEqual(Time, load.Metadata.TimestampUtc);
        }

        [Test]
        public void Save_WhileOffline_MarksPending_AndLaterLoadPushes()
        {
            _cloud.IsAvailable = false;
            SaveReport report = Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));

            Assert.IsTrue(report.SavedLocally);
            Assert.IsFalse(report.PushedToCloud);
            Assert.IsTrue(_local.GetPendingUpload("main"));
            Assert.IsNull(_cloud.PeekStored("main"));

            _cloud.IsAvailable = true;
            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.AreEqual(SyncAction.PushedLocalToCloud, load.SyncAction);
            Assert.IsFalse(_local.GetPendingUpload("main"));
            Assert.IsNotNull(_cloud.PeekStored("main"));
        }

        [Test]
        public void Save_WhenCloudThrows_SurfacesErrorAndKeepsPending()
        {
            _cloud.FailNextOperations = 1;
            SaveReport report = Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));

            Assert.IsTrue(report.SavedLocally);
            Assert.IsFalse(report.PushedToCloud);
            Assert.IsNotNull(report.CloudError);
            Assert.IsTrue(_local.GetPendingUpload("main"));
        }

        [Test]
        public void Load_CloudOnlySave_IsAdoptedLocally()
        {
            var cloudMeta = new SaveMetadata(2, Time.AddHours(-1), TimeSpan.FromHours(9), "writer-2");
            _cloud.SimulateRemoteChange("main", SaveEnvelope.Write(in cloudMeta, Payload));

            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.AreEqual(SaveOrigin.Cloud, load.Origin);
            Assert.AreEqual(SyncAction.PulledCloudToLocal, load.SyncAction);
            Assert.IsFalse(load.HadConflict);
            Assert.IsNotNull(_local.Load("main"));
        }

        [Test]
        public void Load_ConflictRemoteWins_OverwritesLocal()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));

            byte[] remotePayload = Encoding.UTF8.GetBytes("{\"gold\":9999}");
            var remoteMeta = new SaveMetadata(2, Time.AddMinutes(-30), TimeSpan.FromHours(50), "writer-2");
            _cloud.SimulateRemoteChange("main", SaveEnvelope.Write(in remoteMeta, remotePayload));

            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.IsTrue(load.HadConflict);
            Assert.AreEqual(SaveOrigin.Cloud, load.Origin);
            CollectionAssert.AreEqual(remotePayload, load.Payload);
            Assert.AreEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryRead(_local.Load("main")!, out SaveMetadata newLocal, out _));
            Assert.AreEqual(remoteMeta, newLocal);
        }

        [Test]
        public void Load_ConflictLocalWins_PushesToCloud()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(100)));

            var remoteMeta = new SaveMetadata(2, Time.AddHours(1), TimeSpan.FromHours(2), "writer-2");
            _cloud.SimulateRemoteChange("main", SaveEnvelope.Write(in remoteMeta, Payload));
            int savesBefore = _cloud.SaveCallCount;

            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.IsTrue(load.HadConflict);
            Assert.AreEqual(SaveOrigin.Local, load.Origin);
            Assert.AreEqual(SyncAction.PushedLocalToCloud, load.SyncAction);
            Assert.AreEqual(savesBefore + 1, _cloud.SaveCallCount);
            Assert.AreEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryRead(_cloud.PeekStored("main")!, out SaveMetadata cloudMeta, out _));
            Assert.AreEqual("writer-1", cloudMeta.WriterId);
        }

        [Test]
        public void Load_CorruptLocal_IsRescuedByCloud()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));
            _local.CorruptForTesting("main", 20);

            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.IsTrue(load.Found);
            Assert.AreEqual(SaveOrigin.Cloud, load.Origin);
            CollectionAssert.AreEqual(Payload, load.Payload);
        }

        [Test]
        public void Load_MissingSlot_ReportsNotFound()
        {
            LoadReport load = Await(_manager.LoadAsync("nothing-here"));

            Assert.IsFalse(load.Found);
            Assert.IsNull(load.Payload);
        }

        [Test]
        public void Delete_RemovesBothSides()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));

            Assert.IsTrue(Await(_manager.DeleteAsync("main")));
            Assert.IsNull(_local.Load("main"));
            Assert.IsNull(_cloud.PeekStored("main"));
        }

        [Test]
        public void SyncAll_CoversUnionOfLocalAndCloudSlots()
        {
            Await(_manager.SaveAsync("local-slot", Payload, TimeSpan.FromHours(1)));
            var cloudMeta = new SaveMetadata(2, Time, TimeSpan.FromHours(3), "writer-2");
            _cloud.SimulateRemoteChange("cloud-slot", SaveEnvelope.Write(in cloudMeta, Payload));

            var reports = Await(_manager.SyncAllAsync());

            Assert.AreEqual(2, reports.Count);
            Assert.AreEqual(SyncAction.PulledCloudToLocal, reports["cloud-slot"].Action);
            Assert.IsNotNull(_local.Load("cloud-slot"));
        }

        [Test]
        public void RemoteChanged_IsForwardedFromProvider()
        {
            string? changedSlot = null;
            _manager.RemoteChanged += slot => changedSlot = slot;

            _cloud.SimulateRemoteChange("main", SaveEnvelope.Write(new SaveMetadata(1, Time, TimeSpan.Zero, "x"), Payload));

            Assert.AreEqual("main", changedSlot);
        }

        [Test]
        public void InvalidSlotName_Throws()
        {
            Assert.Throws<InvalidSlotNameException>(() =>
                _manager.SaveAsync("bad/slot", Payload, TimeSpan.Zero).GetAwaiter().GetResult());
        }

        [Test]
        public void IdenticalLocalAndCloud_ClearsPendingWithoutSync()
        {
            Await(_manager.SaveAsync("main", Payload, TimeSpan.FromHours(1)));
            _local.SetPendingUpload("main", true); // stale flag

            LoadReport load = Await(_manager.LoadAsync("main"));

            Assert.AreEqual(SyncAction.None, load.SyncAction);
            Assert.IsFalse(load.HadConflict);
            Assert.IsFalse(_local.GetPendingUpload("main"));
        }
    }
}
