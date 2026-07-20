using System.Collections;
using System.Text;
using Microsoft.Extensions.Logging;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Logging;

/// <summary>
/// W6 — Decorates every registered <see cref="ILoggerProvider"/> so that the
/// structured <c>Incident</c> property (and its rendered message text) is masked
/// to its last four characters by default. Full incident values are only emitted
/// when <see cref="LoggingOptions.VerbosePayload"/> is <c>true</c>.
/// </summary>
public sealed class RedactingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;
    private readonly LoggingOptions _options;

    public RedactingLoggerProvider(ILoggerProvider inner, LoggingOptions options)
    {
        _inner = inner;
        _options = options;
    }

    public ILogger CreateLogger(string categoryName)
        => new RedactingLogger(_inner.CreateLogger(categoryName), _options);

    public void Dispose() => _inner.Dispose();
}

public sealed class RedactingLogger : ILogger
{
    private const string IncidentKey = "Incident";
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly ILogger _inner;
    private readonly LoggingOptions _options;

    public RedactingLogger(ILogger inner, LoggingOptions options)
    {
        _inner = inner;
        _options = options;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Verbose mode or non-structured state → pass through unchanged.
        if (_options.VerbosePayload || state is not IReadOnlyList<KeyValuePair<string, object?>> kvps)
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        var hasIncident = false;
        foreach (var kvp in kvps)
        {
            if (kvp.Key == IncidentKey) { hasIncident = true; break; }
        }

        if (!hasIncident)
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
            return;
        }

        var masked = BuildMaskedState(kvps);
        _inner.Log(logLevel, eventId, masked, exception, static (s, _) => s.ToString());
    }

    private static RedactedState BuildMaskedState(IReadOnlyList<KeyValuePair<string, object?>> kvps)
    {
        string? template = null;
        var values = new List<KeyValuePair<string, object?>>(kvps.Count);
        var lookup = new Dictionary<string, object?>(kvps.Count);

        foreach (var kvp in kvps)
        {
            if (kvp.Key == OriginalFormatKey)
            {
                template = kvp.Value as string;
                values.Add(kvp);
                continue;
            }

            var value = kvp.Key == IncidentKey
                ? IncidentMasker.Mask(kvp.Value?.ToString())
                : kvp.Value;

            values.Add(new KeyValuePair<string, object?>(kvp.Key, value));
            lookup[kvp.Key] = value;
        }

        var rendered = template is not null
            ? RenderTemplate(template, lookup)
            : string.Join(" ", lookup.Select(kv => $"{kv.Key}={kv.Value}"));

        return new RedactedState(values, rendered);
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, object?> values)
    {
        var sb = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
                var end = template.IndexOf('}', i + 1);
                if (end < 0) { sb.Append(c); continue; }
                var token = template.Substring(i + 1, end - i - 1);
                var name = token.Split(new[] { ':', ',' }, 2)[0];
                sb.Append(values.TryGetValue(name, out var v) ? v?.ToString() : "{" + token + "}");
                i = end;
            }
            else if (c == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i++; continue; }
                sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private sealed class RedactedState : IReadOnlyList<KeyValuePair<string, object?>>
    {
        private readonly IReadOnlyList<KeyValuePair<string, object?>> _values;
        private readonly string _rendered;

        public RedactedState(IReadOnlyList<KeyValuePair<string, object?>> values, string rendered)
        {
            _values = values;
            _rendered = rendered;
        }

        public KeyValuePair<string, object?> this[int index] => _values[index];
        public int Count => _values.Count;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => _rendered;
    }
}

/// <summary>Masks an incident number to its last four characters (<c>INC0012456 → ***2456</c>).</summary>
public static class IncidentMasker
{
    public static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 4)
        {
            return value;
        }
        return "***" + value[^4..];
    }
}
