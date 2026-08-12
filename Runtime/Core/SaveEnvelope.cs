#nullable enable
using System;
using System.IO;
using System.Text;

namespace SK.CloudSave
{
    public enum EnvelopeReadResult
    {
        Ok,
        TooShort,
        BadMagic,
        UnsupportedFormatVersion,
        Malformed,
        CorruptCrc,
    }

    /// <summary>
    /// Binary envelope wrapped around every save payload. Carries the metadata that
    /// conflict resolvers need, plus a CRC-32 so corrupted saves are detected instead
    /// of being deserialized into garbage game state.
    ///
    /// Layout (little-endian):
    ///   magic          4 bytes  "CSK1"
    ///   formatVersion  1 byte
    ///   schemaVersion  4 bytes  (int32)
    ///   timestampUtc   8 bytes  (int64 ticks)
    ///   totalPlaytime  8 bytes  (int64 ticks)
    ///   writerIdLen    1 byte
    ///   writerId       N bytes  (UTF-8)
    ///   payloadLen     4 bytes  (int32)
    ///   payload        N bytes
    ///   crc32          4 bytes  (over all preceding bytes)
    /// </summary>
    public static class SaveEnvelope
    {
        public const uint Magic = 0x314B5343; // "CSK1" little-endian
        public const byte FormatVersion = 1;
        public const int MaxWriterIdBytes = byte.MaxValue;

        private const int FixedHeaderSize = 4 + 1 + 4 + 8 + 8 + 1; // up to and including writerIdLen
        private const int MinimumSize = FixedHeaderSize + 4 /*payloadLen*/ + 4 /*crc*/;

        public static byte[] Write(in SaveMetadata metadata, ReadOnlySpan<byte> payload)
        {
            byte[] writerIdBytes = Encoding.UTF8.GetBytes(metadata.WriterId ?? string.Empty);
            if (writerIdBytes.Length > MaxWriterIdBytes)
            {
                throw new CloudSaveException($"WriterId is too long ({writerIdBytes.Length} bytes, max {MaxWriterIdBytes}).");
            }

            int totalSize = FixedHeaderSize + writerIdBytes.Length + 4 + payload.Length + 4;
            byte[] buffer = new byte[totalSize];
            int offset = 0;

            WriteUInt32(buffer, ref offset, Magic);
            buffer[offset++] = FormatVersion;
            WriteInt32(buffer, ref offset, metadata.SchemaVersion);
            WriteInt64(buffer, ref offset, metadata.TimestampUtc.Ticks);
            WriteInt64(buffer, ref offset, metadata.TotalPlaytime.Ticks);
            buffer[offset++] = (byte)writerIdBytes.Length;
            writerIdBytes.CopyTo(buffer.AsSpan(offset));
            offset += writerIdBytes.Length;
            WriteInt32(buffer, ref offset, payload.Length);
            payload.CopyTo(buffer.AsSpan(offset));
            offset += payload.Length;

            uint crc = Crc32.Compute(buffer.AsSpan(0, offset));
            WriteUInt32(buffer, ref offset, crc);

            return buffer;
        }

        public static EnvelopeReadResult TryRead(ReadOnlySpan<byte> data, out SaveMetadata metadata, out byte[] payload)
        {
            metadata = default;
            payload = Array.Empty<byte>();

            if (data.Length < MinimumSize)
            {
                return EnvelopeReadResult.TooShort;
            }

            int offset = 0;
            uint magic = ReadUInt32(data, ref offset);
            if (magic != Magic)
            {
                return EnvelopeReadResult.BadMagic;
            }

            byte formatVersion = data[offset++];
            if (formatVersion != FormatVersion)
            {
                return EnvelopeReadResult.UnsupportedFormatVersion;
            }

            int schemaVersion = ReadInt32(data, ref offset);
            long timestampTicks = ReadInt64(data, ref offset);
            long playtimeTicks = ReadInt64(data, ref offset);

            int writerIdLength = data[offset++];
            if (offset + writerIdLength + 4 + 4 > data.Length)
            {
                return EnvelopeReadResult.Malformed;
            }
            string writerId = writerIdLength > 0
                ? Encoding.UTF8.GetString(data.Slice(offset, writerIdLength))
                : string.Empty;
            offset += writerIdLength;

            int payloadLength = ReadInt32(data, ref offset);
            if (payloadLength < 0 || offset + payloadLength + 4 != data.Length)
            {
                return EnvelopeReadResult.Malformed;
            }

            int crcInputLength = offset + payloadLength;
            uint storedCrc = ReadUInt32At(data, crcInputLength);
            uint computedCrc = Crc32.Compute(data.Slice(0, crcInputLength));
            if (storedCrc != computedCrc)
            {
                return EnvelopeReadResult.CorruptCrc;
            }

            if (timestampTicks < DateTime.MinValue.Ticks || timestampTicks > DateTime.MaxValue.Ticks || playtimeTicks < 0)
            {
                return EnvelopeReadResult.Malformed;
            }

            payload = data.Slice(offset, payloadLength).ToArray();
            metadata = new SaveMetadata(
                schemaVersion,
                new DateTime(timestampTicks, DateTimeKind.Utc),
                new TimeSpan(playtimeTicks),
                writerId);
            return EnvelopeReadResult.Ok;
        }

        /// <summary>Reads only the metadata header without copying the payload.
        /// Note: this skips CRC validation (which requires the full buffer scan);
        /// use <see cref="TryRead"/> when integrity matters.</summary>
        public static EnvelopeReadResult TryReadMetadata(ReadOnlySpan<byte> data, out SaveMetadata metadata)
        {
            metadata = default;
            if (data.Length < MinimumSize)
            {
                return EnvelopeReadResult.TooShort;
            }

            int offset = 0;
            if (ReadUInt32(data, ref offset) != Magic)
            {
                return EnvelopeReadResult.BadMagic;
            }
            byte formatVersion = data[offset++];
            if (formatVersion != FormatVersion)
            {
                return EnvelopeReadResult.UnsupportedFormatVersion;
            }

            int schemaVersion = ReadInt32(data, ref offset);
            long timestampTicks = ReadInt64(data, ref offset);
            long playtimeTicks = ReadInt64(data, ref offset);
            int writerIdLength = data[offset++];
            if (offset + writerIdLength > data.Length)
            {
                return EnvelopeReadResult.Malformed;
            }
            string writerId = writerIdLength > 0
                ? Encoding.UTF8.GetString(data.Slice(offset, writerIdLength))
                : string.Empty;

            if (timestampTicks < DateTime.MinValue.Ticks || timestampTicks > DateTime.MaxValue.Ticks || playtimeTicks < 0)
            {
                return EnvelopeReadResult.Malformed;
            }

            metadata = new SaveMetadata(
                schemaVersion,
                new DateTime(timestampTicks, DateTimeKind.Utc),
                new TimeSpan(playtimeTicks),
                writerId);
            return EnvelopeReadResult.Ok;
        }

        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            buffer[offset++] = (byte)value;
            buffer[offset++] = (byte)(value >> 8);
            buffer[offset++] = (byte)(value >> 16);
            buffer[offset++] = (byte)(value >> 24);
        }

        private static void WriteInt32(byte[] buffer, ref int offset, int value) => WriteUInt32(buffer, ref offset, (uint)value);

        private static void WriteInt64(byte[] buffer, ref int offset, long value)
        {
            WriteUInt32(buffer, ref offset, (uint)value);
            WriteUInt32(buffer, ref offset, (uint)(value >> 32));
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            uint value = ReadUInt32At(data, offset);
            offset += 4;
            return value;
        }

        private static uint ReadUInt32At(ReadOnlySpan<byte> data, int offset) =>
            (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

        private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset) => (int)ReadUInt32(data, ref offset);

        private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
        {
            uint low = ReadUInt32(data, ref offset);
            uint high = ReadUInt32(data, ref offset);
            return (long)(((ulong)high << 32) | low);
        }
    }
}
