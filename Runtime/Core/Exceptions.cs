#nullable enable
using System;

namespace SK.CloudSave
{
    /// <summary>Base exception for all CloudSaveKit errors.</summary>
    public class CloudSaveException : Exception
    {
        public CloudSaveException(string message) : base(message) { }
        public CloudSaveException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Thrown when a slot name contains illegal characters or is too long.</summary>
    public class InvalidSlotNameException : CloudSaveException
    {
        public InvalidSlotNameException(string slot)
            : base($"Invalid slot name '{slot}'. Slot names must be 1-64 characters of [A-Za-z0-9_-].") { }
    }

    /// <summary>Thrown when a payload exceeds the backing store's size limit (e.g. iCloud KVS ~1MB).</summary>
    public class PayloadTooLargeException : CloudSaveException
    {
        public int ActualSize { get; }
        public int Limit { get; }

        public PayloadTooLargeException(int actualSize, int limit, string providerName)
            : base($"Save payload of {actualSize} bytes exceeds the {limit} byte limit of provider '{providerName}'. " +
                   "Consider compressing the payload or trimming the save data.")
        {
            ActualSize = actualSize;
            Limit = limit;
        }
    }
}
