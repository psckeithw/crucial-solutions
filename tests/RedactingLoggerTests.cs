using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ServiceNowToAdo.Logging;
using ServiceNowToAdo.Services;
using Xunit;

namespace ServiceNowToAdo.Tests;

public class RedactingLoggerTests
{
    private sealed class CapturingLogger : ILogger
    {
        public string? Message { get; private set; }
        public IReadOnlyList<KeyValuePair<string, object?>>? State { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Message = formatter(state, exception);
            State = state as IReadOnlyList<KeyValuePair<string, object?>>;
        }
    }

    private static void LogIncident(ILogger logger, string incident)
        => logger.LogInformation("Outcome=created Incident={Incident} Project={Project}", incident, "UPO");

    [Fact]
    public void RedactingLogger_MasksIncidentByDefault()
    {
        var inner = new CapturingLogger();
        var logger = new RedactingLogger(inner, new LoggingOptions { VerbosePayload = false });

        LogIncident(logger, "INC0012456");

        Assert.Equal("Outcome=created Incident=***2456 Project=UPO", inner.Message);
        var incidentValue = inner.State!.First(kv => kv.Key == "Incident").Value;
        Assert.Equal("***2456", incidentValue);
    }

    [Fact]
    public void RedactingLogger_VerboseMode_ShowsFull()
    {
        var inner = new CapturingLogger();
        var logger = new RedactingLogger(inner, new LoggingOptions { VerbosePayload = true });

        LogIncident(logger, "INC0012456");

        Assert.Equal("Outcome=created Incident=INC0012456 Project=UPO", inner.Message);
        var incidentValue = inner.State!.First(kv => kv.Key == "Incident").Value;
        Assert.Equal("INC0012456", incidentValue);
    }

    [Fact]
    public void RedactingLogger_NoIncidentField_PassesThroughUnchanged()
    {
        var inner = new CapturingLogger();
        var logger = new RedactingLogger(inner, new LoggingOptions { VerbosePayload = false });

        logger.LogInformation("Outcome=validation-failed Reason={Reason}", "missing-fields");

        Assert.Equal("Outcome=validation-failed Reason=missing-fields", inner.Message);
    }

    [Theory]
    [InlineData("INC0012456", "***2456")]
    [InlineData("2456", "***2456")]
    [InlineData("INC", "INC")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void IncidentMasker_MasksLastFour(string? input, string? expected)
    {
        Assert.Equal(expected, IncidentMasker.Mask(input));
    }
}
