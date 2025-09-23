namespace Resty.Tests;

using System.Net;
using System.Text;
using Moq;
using Moq.Protected;
using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Variables;

public class HttpTestExecutorTests
{
  [Fact]
  public async Task ExecuteTestAsync_ShouldResolveVariablesAndExecuteRequest()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    var responseJson = @"{""id"": 123, ""token"": ""abc123"", ""status"": ""success""}";

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_login",
      Method = "POST",
      Url = "$host/api/login",
      Body = @"{""username"": ""$username"", ""password"": ""secret""}",
      Extractors = new Dictionary<string, string> {
        ["user_id"] = "$.id",
        ["auth_token"] = "$.token"
      }
    };

    var variableStore = new VariableStore();
    variableStore.SetIncludedVariables(new Dictionary<string, object> {
      ["host"] = "https://api.example.com",
      ["username"] = "testuser"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
    Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    Assert.Equal(responseJson, result.ResponseBody);

    // Verify variable extraction
    Assert.Equal(2, result.ExtractedVariables.Count);
    Assert.Equal(123L, result.ExtractedVariables["user_id"]);
    Assert.Equal("abc123", result.ExtractedVariables["auth_token"]);

    // Verify request was correctly formed
    Assert.NotNull(result.RequestInfo);
    Assert.Equal("POST", result.RequestInfo.Method);
    Assert.Equal("https://api.example.com/api/login", result.RequestInfo.Url);
    Assert.Contains("testuser", result.RequestInfo.Body!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldValidateExpectedHeaders_Pass()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    HttpRequestMessage? capturedRequest = null;

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .Callback<HttpRequestMessage, CancellationToken>(( req, _ ) => capturedRequest = req)
      .ReturnsAsync(() => {
        var resp = new HttpResponseMessage {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        resp.Headers.TryAddWithoutValidation("X-Custom-Header", " expected ");
        return resp;
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_pass",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> {
          ["content-type"] = "application/json; charset=utf-8",
          ["X-CUSTOM-HEADER"] = "expected"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
    Assert.NotNull(capturedRequest);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldValidateExpectedHeaders_FailOnMissing()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_missing",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> {
          ["X-Required"] = "value"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("missing header 'X-Required'", result.ErrorMessage!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldValidateExpectedHeaders_FailOnMismatch()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => {
        var resp = new HttpResponseMessage {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        resp.Headers.TryAddWithoutValidation("X-Env", "prod");
        return resp;
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_mismatch",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> {
          ["X-Env"] = "stage"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("header 'X-Env' expected 'stage' but got 'prod'", result.ErrorMessage!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldResolveVariablesInExpectedHeaders()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => {
        var resp = new HttpResponseMessage {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        resp.Headers.TryAddWithoutValidation("X-Token", "abc123");
        return resp;
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_vars",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> {
          ["X-Token"] = "$token_val"
        }
      }
    };

    var variableStore = new VariableStore();
    variableStore.SetIncludedVariables(new Dictionary<string, object> {
      ["token_val"] = "abc123"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldGateHeaderValidationOnStatus()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => new HttpResponseMessage {
        StatusCode = HttpStatusCode.BadRequest,
        ReasonPhrase = "Bad Request",
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_gated",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Status = 200,
        Headers = new Dictionary<string, string> {
          ["X-Token"] = "abc123"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("Expected status 200 but got 400", result.ErrorMessage!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldValidateHeadersWhenNoStatusOn2xx()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => {
        var resp = new HttpResponseMessage {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        resp.Headers.TryAddWithoutValidation("X-Check", "ok");
        return resp;
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_no_status_2xx",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        // No explicit status, so 2xx semantics apply
        Headers = new Dictionary<string, string> {
          ["X-Check"] = "ok"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldNotValidateHeadersWhenNoStatusAndNon2xx()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(() => new HttpResponseMessage {
        StatusCode = HttpStatusCode.NotFound,
        ReasonPhrase = "Not Found",
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_headers_no_status_non2xx",
      Method = "GET",
      Url = "https://api.example.com/h",
      Expect = new ExpectDefinition {
        Headers = new Dictionary<string, string> {
          ["X-Check"] = "ok"
        }
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("HTTP 404 Not Found", result.ErrorMessage!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldHandleAuthorizationHeader()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    HttpRequestMessage? capturedRequest = null;

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .Callback<HttpRequestMessage, CancellationToken>(( req, _ ) => capturedRequest = req)
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_auth",
      Method = "GET",
      Url = "https://api.example.com/protected",
      Authorization = "Bearer $token"
    };

    var variableStore = new VariableStore();
    variableStore.SetCapturedVariables(new Dictionary<string, object> {
      ["token"] = "jwt_token_123"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
    Assert.NotNull(capturedRequest);
    Assert.NotNull(capturedRequest.Headers.Authorization);
    Assert.Equal("Bearer", capturedRequest.Headers.Authorization.Scheme);
    Assert.Equal("jwt_token_123", capturedRequest.Headers.Authorization.Parameter);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldHandleCustomHeaders()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    HttpRequestMessage? capturedRequest = null;

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .Callback<HttpRequestMessage, CancellationToken>(( req, _ ) => capturedRequest = req)
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_custom_headers",
      Method = "POST",
      Url = "https://api.example.com/data",
      Headers = new Dictionary<string, string> {
        ["X-API-Key"] = "$api_key",
        ["X-Client-Version"] = "1.0.0"
      },
      Body = @"{""data"": ""test""}"
    };

    var variableStore = new VariableStore();
    variableStore.SetIncludedVariables(new Dictionary<string, object> {
      ["api_key"] = "key_12345"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
    Assert.NotNull(capturedRequest);

    var apiKeyHeader = capturedRequest.Headers.GetValues("X-API-Key").FirstOrDefault();
    var versionHeader = capturedRequest.Headers.GetValues("X-Client-Version").FirstOrDefault();

    Assert.Equal("key_12345", apiKeyHeader);
    Assert.Equal("1.0.0", versionHeader);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldHandleHttpErrors()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.Unauthorized,
        ReasonPhrase = "Unauthorized",
        Content = new StringContent(@"{""error"": ""Invalid credentials""}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_failure",
      Method = "POST",
      Url = "https://api.example.com/login",
      Body = @"{""username"": ""invalid"", ""password"": ""wrong""}"
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    Assert.Contains("HTTP 401 Unauthorized", result.ErrorMessage!);
    Assert.Contains("Invalid credentials", result.ResponseBody!);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldHandleNetworkExceptions()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ThrowsAsync(new HttpRequestException("Connection failed"));

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_network_error",
      Method = "GET",
      Url = "https://unreachable.example.com/api"
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("Request failed: Connection failed", result.ErrorMessage!);
    Assert.NotNull(result.Exception);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldFailWhenCaptureMissingOn2xx()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    var responseJson = @"{""data"": {""value"": 42}}";

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_capture_strict_on_2xx",
      Method = "GET",
      Url = "https://api.example.com/data",
      Extractors = new Dictionary<string, string> {
        ["valid_value"] = "$.data.value",
        ["invalid_value"] = "$.nonexistent.path"
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("Capture failed", result.ErrorMessage!);
  }
  [Fact]
  public async Task ExecuteTestAsync_ShouldHandleEnvironmentVariables()
  {
    // Arrange
    Environment.SetEnvironmentVariable("TEST_API_KEY", "env_key_123");

    try {
      var mockHandler = new Mock<HttpMessageHandler>();
      HttpRequestMessage? capturedRequest = null;

      mockHandler.Protected()
        .Setup<Task<HttpResponseMessage>>(
          "SendAsync",
          ItExpr.IsAny<HttpRequestMessage>(),
          ItExpr.IsAny<CancellationToken>())
        .Callback<HttpRequestMessage, CancellationToken>(( req, _ ) => capturedRequest = req)
        .ReturnsAsync(new HttpResponseMessage {
          StatusCode = HttpStatusCode.OK,
          Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });

      var httpClient = new HttpClient(mockHandler.Object);
      var executor = new HttpTestExecutor(httpClient);

      var test = new HttpTest {
        Name = "test_env_vars",
        Method = "GET",
        Url = "https://api.example.com/secure",
        Headers = new Dictionary<string, string> {
          ["X-API-Key"] = "$env:TEST_API_KEY"
        }
      };

      var variableStore = new VariableStore();

      // Act
      var result = await executor.ExecuteTestAsync(test, variableStore);

      // Assert
      Assert.True(result.Passed);
      Assert.NotNull(capturedRequest);

      var apiKeyHeader = capturedRequest.Headers.GetValues("X-API-Key").FirstOrDefault();
      Assert.Equal("env_key_123", apiKeyHeader);
    } finally {
      Environment.SetEnvironmentVariable("TEST_API_KEY", null);
    }
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldPassWhenExpectedStatusMatchesNon2xx()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();
    var responseJson = @"{""error"": ""not found""}";

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.NotFound,
        ReasonPhrase = "Not Found",
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_expected_404",
      Method = "GET",
      Url = "https://api.example.com/missing",
      ExpectedStatus = 404,
      Extractors = new Dictionary<string, string> {
        ["error_msg"] = "$.error"
      }
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Passed);
    Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    Assert.Single(result.ExtractedVariables);
    Assert.Equal("not found", result.ExtractedVariables["error_msg"]);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldFailWhenExpectedStatusMismatch()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.BadRequest,
        ReasonPhrase = "Bad Request",
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "test_expected_404_mismatch",
      Method = "GET",
      Url = "https://api.example.com/endpoint",
      ExpectedStatus = 404
    };

    var variableStore = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, variableStore);

    // Assert
    Assert.True(result.Failed);
    Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    Assert.Contains("Expected status 404 but got 400", result.ErrorMessage!);
  }
}
