namespace Resty.Tests;

using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Variables;

public class RetryPolicyExecutionTests
{
  [Fact]
  public async Task StructuredRetry_RetriesConfiguredStatusWithFixedDelay()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse { Status = 404 },
      new MockResponse { Status = 200, Body = new { ready = true } }) with {
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = "${{ milliseconds(25) }}",
        Statuses = new ExpectedStatusCodes(new[] { 404 })
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Passed);
    Assert.Equal("Succeeded on attempt 2/2", result.ErrorMessage);
    Assert.Equal(new[] { TimeSpan.FromMilliseconds(25) }, executor.Delays);
  }

  [Fact]
  public async Task StructuredRetry_DoesNotRetryUnconfiguredStatus()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse { Status = 404, Body = new { attempt = 1 } },
      new MockResponse { Status = 200, Body = new { attempt = 2 } }) with {
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = TimeSpan.FromMilliseconds(25),
        Statuses = new ExpectedStatusCodes(new[] { 409 })
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Failed);
    Assert.Equal(404, (int)result.StatusCode!);
    Assert.Empty(executor.Delays);
    Assert.Contains("\"attempt\":1", result.ResponseBody);
  }

  [Fact]
  public async Task StructuredRetry_RetriesJsonExpectationFailureWhenEnabled()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse { Status = 200, Body = new { ready = false } },
      new MockResponse { Status = 200, Body = new { ready = true } }) with {
      Expect = new ExpectDefinition {
        Status = 200,
        Values = [new ValueExpectation { Key = "$.ready", Op = "equals", Value = true }]
      },
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = TimeSpan.FromMilliseconds(10),
        ExpectationFailures = true
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Passed);
    Assert.Equal("Succeeded on attempt 2/2", result.ErrorMessage);
    Assert.Equal(new[] { TimeSpan.FromMilliseconds(10) }, executor.Delays);
  }

  [Fact]
  public async Task StructuredRetry_RetriesHeaderExpectationFailureWhenEnabled()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse {
        Status = 200,
        Headers = new Dictionary<string, string> { ["X-State"] = "pending" }
      },
      new MockResponse {
        Status = 200,
        Headers = new Dictionary<string, string> { ["X-State"] = "ready" }
      }) with {
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> { ["X-State"] = "ready" }
      },
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = TimeSpan.FromMilliseconds(10),
        ExpectationFailures = true
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Passed);
    Assert.Equal(new[] { TimeSpan.FromMilliseconds(10) }, executor.Delays);
  }

  [Fact]
  public async Task StructuredRetry_DoesNotRetryExpectationFailureWhenDisabled()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse { Status = 200, Body = new { ready = false } },
      new MockResponse { Status = 200, Body = new { ready = true } }) with {
      Expect = new ExpectDefinition {
        Status = 200,
        Values = [new ValueExpectation { Key = "$.ready", Op = "equals", Value = true }]
      },
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = TimeSpan.FromMilliseconds(10),
        ExpectationFailures = false
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Failed);
    Assert.Empty(executor.Delays);
  }

  [Fact]
  public async Task WaitBefore_RunsOnceBeforeRetries()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(
      new MockResponse { Status = 500 },
      new MockResponse { Status = 200 }) with {
      WaitBefore = "${{ milliseconds(250) }}",
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = "${{ milliseconds(10) }}"
      }
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Passed);
    Assert.Equal(
      new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(10) },
      executor.Delays);
  }

  [Fact]
  public async Task RetryDelay_HonorsCancellation()
  {
    var executor = new RecordingHttpTestExecutor {
      DelayHandler = ( _, cancellationToken ) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
    };
    var test = CreateSequenceTest(
      new MockResponse { Status = 500 },
      new MockResponse { Status = 200 }) with {
      Retry = new RetryPolicyDefinition {
        Attempts = 2,
        Delay = TimeSpan.FromSeconds(1)
      }
    };
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      executor.ExecuteTestAsync(test, new VariableStore(), cancellationToken: cancellation.Token));
  }

  [Fact]
  public async Task InvalidWaitBefore_FailsBeforeSendingRequest()
  {
    var executor = new RecordingHttpTestExecutor();
    var test = CreateSequenceTest(new MockResponse { Status = 200 }) with {
      WaitBefore = "${{ 1 + 1 }}"
    };

    var result = await executor.ExecuteTestAsync(test, new VariableStore());

    Assert.True(result.Failed);
    Assert.Contains("wait_before must resolve to a duration", result.ErrorMessage);
    Assert.Empty(executor.Delays);
    Assert.Null(result.RequestInfo);
  }

  private static HttpTest CreateSequenceTest( params MockResponse[] responses )
  {
    return new HttpTest {
      Name = $"retry_{Guid.NewGuid():N}",
      Method = "GET",
      Url = "https://api.example.com/resource",
      MockOnly = true,
      InlineMock = new InlineMockDefinition { Sequence = responses.ToList() },
      SourceFile = "retry.resty",
      SourceLine = 1
    };
  }

  private sealed class RecordingHttpTestExecutor : HttpTestExecutor
  {
    public RecordingHttpTestExecutor()
      : base(new HttpClient(), enableMocking: true) { }

    public List<TimeSpan> Delays { get; } = new();
    public Func<TimeSpan, CancellationToken, Task>? DelayHandler { get; init; }

    protected override Task DelayAsync( TimeSpan delay, CancellationToken cancellationToken )
    {
      Delays.Add(delay);
      return DelayHandler?.Invoke(delay, cancellationToken) ?? Task.CompletedTask;
    }
  }
}
