using System.Text;
using Moq;
using Moq.Protected;
using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Variables;

namespace Resty.Tests;

public class MockingTests
{
  [Fact]
  public async Task InlineMockOnly_NoHttpMethod_ShouldPass()
  {
    var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
    var executor = new HttpTestExecutor(httpClient, enableMocking: false);

    var test = new HttpTest {
      Name = "mock_inline",
      MockOnly = true,
      InlineMock = new InlineMockDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = new { ok = true }
      },
      SourceFile = "inline.resty",
      SourceLine = 1
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);

    Assert.True(result.Passed);
    Assert.Equal(200, (int)result.StatusCode!);
    Assert.Contains("ok", result.ResponseBody);
  }

  [Fact]
  public void MockOnly_NoInline_NoMethod_ShouldFailAtParse()
  {
    // Create a block equivalent to:
    // test: bad
    // mock_only: true
    // (no get/post, no mock)
    var block = new YamlBlock {
      Test = "bad",
      MockOnly = true,
      // MockOnly true but no Mock and no method/url
      Variables = null,
      Headers = null,
      Body = null,
    } with { _method = string.Empty };

    Assert.Throws<InvalidOperationException>(() =>
    {
      _ = HttpTest.FromYamlBlock(block, "file.resty", 1);
    });
  }

  [Fact]
  public async Task FileMocks_WithEnableMocking_ShouldServeMock()
  {
    var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
    var executor = new HttpTestExecutor(httpClient, enableMocking: true);

    var test = new HttpTest {
      Name = "file_mocks",
      Method = "GET",
      Url = "https://api.example.com/users?limit=1",
      FileMocks = new List<FileMockDefinition> {
        new() {
          Method = "GET",
          Url = "https://api.example.com/users?limit=1",
          Status = 200,
          Body = new { users = new[]{ new { id = 1 } } }
        }
      },
      SourceFile = "file.resty",
      SourceLine = 10
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
    Assert.Contains("users", result.ResponseBody);
  }

  [Fact]
  public async Task Sequence_WithRetry_ShouldReturn200OnSecondAttempt()
  {
    var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
    var executor = new HttpTestExecutor(httpClient, enableMocking: true);

    var test = new HttpTest {
      Name = "seq_retry",
      Method = "GET",
      Url = "https://api.example.com/thing",
      FileMocks = new List<FileMockDefinition> {
        new() {
          Method = "GET",
          Url = "https://api.example.com/thing",
          Sequence = new List<MockResponse> {
            new() { Status = 429, Body = new { error = "rate" } },
            new() { Status = 200, Body = new { ok = true } }
          }
        }
      },
      SourceFile = "file.resty",
      SourceLine = 20
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store, retryCount: 1);
    Assert.True(result.Passed);
    Assert.Contains("ok", result.ResponseBody);
  }

  [Fact]
  public async Task MocksFiles_Duplicates_LastWins_AndWarns()
  {
    // Create two temp mock files with the same method+url but different statuses
    var dir = Path.GetTempPath();
    var f1 = Path.Combine(dir, $"m1_{Guid.NewGuid():N}.json");
    var f2 = Path.Combine(dir, $"m2_{Guid.NewGuid():N}.json");

    var content1 = "[ { \"method\":\"GET\", \"url\":\"https://x/a\", \"status\":201 } ]";
    var content2 = "[ { \"method\":\"GET\", \"url\":\"https://x/a\", \"status\":200 } ]";
    File.WriteAllText(f1, content1);
    File.WriteAllText(f2, content2);

    try {
      var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
      var executor = new HttpTestExecutor(httpClient, enableMocking: true);
      var test = new HttpTest {
        Name = "dup_warn",
        Method = "GET",
        Url = "https://x/a",
        MockFiles = new List<string> { f1, f2 },
        SourceFile = Path.Combine(dir, "x.resty"),
        SourceLine = 1
      };

      var store = new VariableStore();

      using var sw = new StringWriter();
      var orig = Console.Out;
      Console.SetOut(sw);

      var result = await executor.ExecuteTestAsync(test, store);

      Console.SetOut(orig);

      Assert.True(result.Passed);
      Assert.Equal(200, (int)result.StatusCode!); // last wins
      Assert.Contains("duplicate mock", sw.ToString(), StringComparison.OrdinalIgnoreCase);
    } finally {
      if (File.Exists(f1)) File.Delete(f1);
      if (File.Exists(f2)) File.Delete(f2);
    }
  }

  [Fact]
  public async Task MocksFiles_Variables_AreResolvedAtServeTime()
  {
    var dir = Path.GetTempPath();
    var f = Path.Combine(dir, $"mv_{Guid.NewGuid():N}.json");
    var content = "[ { \"method\":\"GET\", \"url\":\"$base/$path\", \"status\":200, \"body\": { \"val\": \"$val\" } } ]";
    File.WriteAllText(f, content);

    try {
      var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object);
      var executor = new HttpTestExecutor(httpClient, enableMocking: true);
      var test = new HttpTest {
        Name = "vars_mock",
        Method = "GET",
        Url = "https://api/v1",
        MockFiles = new List<string> { f },
        SourceFile = Path.Combine(dir, "y.resty"),
        SourceLine = 1
      };

      var store = new VariableStore();
      store.SetIncludedVariables(new Dictionary<string, object> {
        ["base"] = "https://api",
        ["path"] = "v1",
        ["val"] = "hello"
      });

      var result = await executor.ExecuteTestAsync(test, store);
      Assert.True(result.Passed);
      Assert.Contains("hello", result.ResponseBody);
    } finally {
      if (File.Exists(f)) File.Delete(f);
    }
  }
}
