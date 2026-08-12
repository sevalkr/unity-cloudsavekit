#nullable enable
namespace SK.CloudSave
{
    /// <summary>Validation for save slot names, kept restrictive so that names are
    /// safe as file names, iCloud KVS keys and Play Games saved-game file names alike.</summary>
    public static class SlotName
    {
        public const int MaxLength = 64;

        public static bool IsValid(string? slot)
        {
            if (string.IsNullOrEmpty(slot) || slot!.Length > MaxLength)
            {
                return false;
            }
            foreach (char c in slot)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }
            return true;
        }

        public static void ThrowIfInvalid(string? slot)
        {
            if (!IsValid(slot))
            {
                throw new InvalidSlotNameException(slot ?? "<null>");
            }
        }
    }
}
