#nullable enable
using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace SK.CloudSave.Tests
{
    public class SaveEnvelopeTests
    {
        private static readonly SaveMetadata Meta =
            new SaveMetadata(3, new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(41.5), "device-abc");
        private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"gold\":1200,\"sheep\":7}");

        [Test]
        public void Roundtrip_PreservesMetadataAndPayload()
        {
            byte[] envelope = SaveEnvelope.Write(in Meta, Payload);

            Assert.AreEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryRead(envelope, out SaveMetadata meta, out byte[] payload));
            Assert.AreEqual(Meta, meta);
            CollectionAssert.AreEqual(Payload, payload);
        }

        [Test]
        public void CorruptedByte_IsDetectedByCrc()
        {
            byte[] envelope = SaveEnvelope.Write(in Meta, Payload);
            envelope[envelope.Length / 2] ^= 0xFF;

            Assert.AreEqual(EnvelopeReadResult.CorruptCrc, SaveEnvelope.TryRead(envelope, out _, out _));
        }

        [Test]
        public void TruncatedEnvelope_IsRejected()
        {
            byte[] envelope = SaveEnvelope.Write(in Meta, Payload);
            byte[] truncated = envelope.Take(envelope.Length - 3).ToArray();

            EnvelopeReadResult result = SaveEnvelope.TryRead(truncated, out _, out _);
            Assert.That(result, Is.EqualTo(EnvelopeReadResult.Malformed).Or.EqualTo(EnvelopeReadResult.TooShort));
        }

        [Test]
        public void RandomGarbage_IsRejected()
        {
            byte[] garbage = new byte[64];
            new Random(42).NextBytes(garbage);

            Assert.AreNotEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryRead(garbage, out _, out _));
        }

        [Test]
        public void MetadataOnlyRead_MatchesFullRead()
        {
            byte[] envelope = SaveEnvelope.Write(in Meta, Payload);

            Assert.AreEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryReadMetadata(envelope, out SaveMetadata meta));
            Assert.AreEqual(Meta, meta);
        }

        [Test]
        public void EmptyPayloadAndEmptyWriter_AreValid()
        {
            var meta = new SaveMetadata(1, DateTime.UtcNow, TimeSpan.Zero, "");
            byte[] envelope = SaveEnvelope.Write(in meta, ReadOnlySpan<byte>.Empty);

            Assert.AreEqual(EnvelopeReadResult.Ok, SaveEnvelope.TryRead(envelope, out _, out byte[] payload));
            Assert.AreEqual(0, payload.Length);
        }
    }

    public class SlotNameTests
    {
        [TestCase("main")]
        [TestCase("slot-2_B")]
        [TestCase("A")]
        public void ValidNames_Pass(string slot) => Assert.IsTrue(SlotName.IsValid(slot));

        [TestCase("")]
        [TestCase("a/b")]
        [TestCase("a b")]
        [TestCase("türkçe")]
        [TestCase(null!)]
        public void InvalidNames_Fail(string? slot) => Assert.IsFalse(SlotName.IsValid(slot));

        [Test]
        public void TooLongName_Fails() => Assert.IsFalse(SlotName.IsValid(new string('x', 65)));
    }
}
