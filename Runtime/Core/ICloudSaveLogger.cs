#nullable enable
namespace SK.CloudSave
{
    public interface ICloudSaveLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public sealed class NullCloudSaveLogger : ICloudSaveLogger
    {
        public static readonly NullCloudSaveLogger Instance = new NullCloudSaveLogger();
        private NullCloudSaveLogger() { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
    }
}
