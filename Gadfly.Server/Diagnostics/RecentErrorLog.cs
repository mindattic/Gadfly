using System.Collections.Concurrent;

namespace Gadfly.Server.Diagnostics;

/// <summary>One error the app logged, as the system status page shows it (GAD-T355).</summary>
public sealed record LoggedError(DateTimeOffset At, string Level, string Category, string Message, string? Exception);

/// <summary>
/// The last errors this server logged, kept in memory for the system status page (GAD-T302/GAD-T355): a logger provider that
/// keeps only Error and Critical entries, newest last, up to <see cref="Capacity"/>. Nothing is written anywhere; the
/// list starts empty on every restart and each server instance has its own. Messages are cut to a length a page can show.
/// It sees every organization's errors, so only the platform admin is shown its text.
/// </summary>
public sealed class RecentErrorLog : ILoggerProvider
{
    public const int Capacity = 50;
    public const int MaxMessageLength = 600;

    private readonly ConcurrentQueue<LoggedError> _entries = new();
    private readonly TimeProvider _clock;

    public RecentErrorLog(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>The kept errors, newest first.</summary>
    public IReadOnlyList<LoggedError> Recent() => _entries.Reverse().ToList();

    /// <summary>How many kept errors were logged at or after <paramref name="since"/>.</summary>
    public int CountSince(DateTimeOffset since) => _entries.Count(e => e.At >= since);

    public void Add(LogLevel level, string category, string message, Exception? exception)
    {
        if (level < LogLevel.Error) return;
        _entries.Enqueue(new LoggedError(_clock.GetUtcNow(), level.ToString(), category, Cut(message),
            exception is null ? null : Cut($"{exception.GetType().Name}: {exception.Message}")));
        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    private static string Cut(string s) => s.Length > MaxMessageLength ? s[..MaxMessageLength] + "…" : s;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(RecentErrorLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message;
            try { message = formatter(state, exception); }
            catch (FormatException) { message = state?.ToString() ?? ""; }
            owner.Add(logLevel, category, message, exception);
        }
    }
}
