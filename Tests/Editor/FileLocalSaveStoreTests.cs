#nullable enable
using System;
using System.IO;
using System.Linq;
using SK.CloudSave.Providers;
using NUnit.Framework;

namespace SK.CloudSave.Tests
{
    public class FileLocalSaveStoreTests
    {
        private string _tempDir = null!;
        private FileLocalSaveStore _store = null!;
        private static readonly byte[] Data = { 1, 2, 3, 4, 5 };

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "csk-test-" + Guid.NewGuid().ToString("N"));
            _store = new FileLocalSaveStore(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void Load_MissingSlot_ReturnsNull() => Assert.IsNull(_store.Load("main"));

        [Test]
        public void SaveAndLoad_Roundtrips()
        {
            _store.Save("main", Data);
            CollectionAssert.AreEqual(Data, _store.Load("main"));
        }

        [Test]
        public void Save_Overwrites_AndLeavesNoTempFiles()
        {
            _store.Save("main", Data);
            byte[] updated = { 9, 9, 9 };
            _store.Save("main", updated);

            CollectionAssert.AreEqual(updated, _store.Load("main"));
            Assert.IsFalse(Directory.GetFiles(_tempDir).Any(f => f.EndsWith(".tmp")));
        }

        [Test]
        public void PendingFlag_PersistsAndClears()
        {
            _store.SetPendingUpload("main", true);
            Assert.IsTrue(_store.GetPendingUpload("main"));

            // A fresh instance over the same directory must see the flag (survives restarts).
            var reopened = new FileLocalSaveStore(_tempDir);
            Assert.IsTrue(reopened.GetPendingUpload("main"));

            reopened.SetPendingUpload("main", false);
            Assert.IsFalse(_store.GetPendingUpload("main"));
        }

        [Test]
        public void ListSlots_ReturnsSavedSlotsOnly()
        {
            _store.Save("main", Data);
            _store.Save("second", Data);
            _store.SetPendingUpload("third", true); // pending marker alone is not a save

            CollectionAssert.AreEquivalent(new[] { "main", "second" }, _store.ListSlots());
        }

        [Test]
        public void Delete_RemovesSaveAndPendingFlag()
        {
            _store.Save("main", Data);
            _store.SetPendingUpload("main", true);

            Assert.IsTrue(_store.Delete("main"));
            Assert.IsNull(_store.Load("main"));
            Assert.IsFalse(_store.GetPendingUpload("main"));
            Assert.IsFalse(_store.Delete("main"));
        }

        [Test]
        public void InvalidSlotName_Throws()
        {
            Assert.Throws<InvalidSlotNameException>(() => _store.Save("../escape", Data));
            Assert.Throws<InvalidSlotNameException>(() => _store.Load("a/b"));
        }
    }
}
