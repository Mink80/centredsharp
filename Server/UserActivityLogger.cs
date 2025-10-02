namespace CentrED.Server;

public enum UserActivityEvent
{
    LOGIN,
    LOGOUT,
    IDLE,
    ACTIVE
}

public class UserActivityLogger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly string _filePath;

    public UserActivityLogger(string filePath)
    {
        _filePath = filePath;

        // Create or append to the log file
        _writer = new StreamWriter(_filePath, append: true)
        {
            AutoFlush = true // Ensure writes are immediately flushed for crash resilience
        };

        // Write header if file is empty
        var fileInfo = new FileInfo(_filePath);
        if (fileInfo.Length == 0)
        {
            lock (_lock)
            {
                _writer.WriteLine("Timestamp,Username,Event,Details");
            }
        }
    }

    public void LogEvent(string username, UserActivityEvent eventType, string details = "")
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var line = $"{timestamp},{username},{eventType},{details}";
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
    }
}
