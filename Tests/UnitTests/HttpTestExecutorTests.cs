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
  public async Task ExpectValues_ShouldSupport_ArrayLengthFunction()
  {
    // Arrange
    var responseJson = "{\"items\":[1,2,3,4]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_array_length",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.items.length()", Op = "equals", Value = 4 },
          new() { Key = "$.items.length()", Op = "greater_than", Value = 2 },
          new() { Key = "$.items.length()", Op = "less_than", Value = 10 }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

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
      .ReturnsAsync(() =>
      {
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
      .ReturnsAsync(() =>
      {
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
      .ReturnsAsync(() =>
      {
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
      .ReturnsAsync(() =>
      {
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

  // ===== expect.values tests =====

  [Fact]
  public async Task ExpectValues_ShouldPass_Equals_String_IgnoreCase_DefaultTrue()
  {
    // Arrange
    var responseJson = "{\"name\":\"TestUser\"}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_equals_ignorecase",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.name", Op = "equals", Value = "testuser" } // ignore_case defaults true
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldFail_Equals_String_CaseSensitive()
  {
    // Arrange
    var responseJson = "{\"name\":\"TestUser\"}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_equals_casesensitive",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.name", Op = "equals", Value = "testuser", IgnoreCase = false }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("expected equals", result.ErrorMessage!);
  }

  [Fact]
  public async Task ExpectValues_ShouldPass_Numeric_Relational()
  {
    // Arrange
    var responseJson = "{\"age\":25}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_numeric_rel",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.age", Op = "greater_than", Value = 18 },
          new() { Key = "$.age", Op = "less_than_or_equal", Value = 25 }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }


  [Fact]
  public async Task ExpectValues_ShouldHandle_Exists_And_NotExists()
  {
    // Arrange
    var responseJson = "{\"present\":1}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_exists",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.present", Op = "exists" },
          new() { Key = "$.missing", Op = "not_exists" }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Array_AnyMatch()
  {
    // Arrange
    var responseJson = "{\"items\":[{\"name\":\"alpha\"},{\"name\":\"beta-promo\"}]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_array_any",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.items[*].name", Op = "endswith", Value = "-promo" }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldCapture_With_StoreAs()
  {
    // Arrange
    var responseJson = "{\"response\":{\"id\":12345}}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_store_as",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.response.id", Op = "equals", Value = 12345, StoreAs = "response_id" }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
    Assert.True(result.ExtractedVariables.ContainsKey("response_id"));
    Assert.Equal(12345L, result.ExtractedVariables["response_id"]); // JToken int → long
  }

  [Fact]
  public async Task ExpectValues_ShouldHandle_Null_And_Empty()
  {
    // Arrange
    var responseJson = "{\"nickname\":\"\",\"deleted_at\":null}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_null_empty",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.nickname", Op = "equals", Value = "$empty" },
          new() { Key = "$.deleted_at", Op = "equals", Value = "$null" }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldSerializeStructuredJsonBody()
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
      Name = "structured_json_body",
      Method = "POST",
      Url = "https://api.example.com/login",
      ContentType = "application/json",
      RawBody = new Dictionary<string, object?> {
        ["username"] = "$user",
        ["password"] = "$pass"
      }
    };

    var store = new VariableStore();
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["user"] = "alice",
      ["pass"] = "secret"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
    Assert.NotNull(capturedRequest);
    var sent = await capturedRequest!.Content!.ReadAsStringAsync();
    Assert.Contains("\"username\":\"alice\"", sent);
    Assert.Contains("\"password\":\"secret\"", sent);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldSerializeStructuredFormBody()
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
      Name = "structured_form_body",
      Method = "POST",
      Url = "https://api.example.com/login",
      ContentType = "application/x-www-form-urlencoded",
      RawBody = new Dictionary<string, object?> {
        ["username"] = "$user",
        ["password"] = "$pass"
      }
    };

    var store = new VariableStore();
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["user"] = "alice",
      ["pass"] = "secret!"
    });

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
    Assert.NotNull(capturedRequest);
    var sent = await capturedRequest!.Content!.ReadAsStringAsync();
    Assert.Contains("username=alice", sent);
    Assert.Contains("password=secret%21", sent);
  }

  [Fact]
  public async Task ExecuteTestAsync_ShouldErrorOnStructuredBodyWithUnsupportedContentType()
  {
    // Arrange
    var mockHandler = new Mock<HttpMessageHandler>();

    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>(
        "SendAsync",
        ItExpr.IsAny<HttpRequestMessage>(),
        ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });

    var httpClient = new HttpClient(mockHandler.Object);
    var executor = new HttpTestExecutor(httpClient);

    var test = new HttpTest {
      Name = "structured_bad_type",
      Method = "POST",
      Url = "https://api.example.com/login",
      ContentType = "text/plain",
      RawBody = new Dictionary<string, object?> { ["a"] = 1 }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Failed);
    Assert.Contains("Structured body is only supported", result.ErrorMessage!);
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

  [Fact]
  public async Task ExpectValues_ShouldSupport_StringLengthFunction()
  {
    // Arrange
    var responseJson = "{\"name\":\"abc\"}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_string_length",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.name.length()", Op = "equals", Value = 3 },
          new() { Key = "$.name.length()", Op = "greater_than", Value = 0 },
          new() { Key = "$.name.length()", Op = "less_than", Value = 10 }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_KeysAndDistinct_LengthChain()
  {
    // Arrange
    var responseJson = "{\"obj\":{\"a\":1,\"b\":2},\"nums\":[1,2,2,3]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_chain_keys_distinct_length",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.obj.keys().length()", Op = "equals", Value = 2 },
          new() { Key = "$.nums.distinct().length()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_NumericAggregates_OnArrays()
  {
    // Arrange
    var responseJson = "{\"nums\":[1,2,3]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_numeric_aggregates",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.nums.sum()", Op = "equals", Value = 6 },
          new() { Key = "$.nums.avg()", Op = "equals", Value = 2 },
          new() { Key = "$.nums.min()", Op = "equals", Value = 1 },
          new() { Key = "$.nums.max()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();

    // Act
    var result = await executor.ExecuteTestAsync(test, store);

    // Assert
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Empty_Function()
  {
    var responseJson = "{\"a\":[],\"b\":\"\",\"c\":null,\"d\":{},\"e\":[1]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_empty",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.a.empty()", Op = "equals", Value = true },
          new() { Key = "$.b.empty()", Op = "equals", Value = true },
          new() { Key = "$.c.empty()", Op = "equals", Value = true },
          new() { Key = "$.d.empty()", Op = "equals", Value = true },
          new() { Key = "$.e.empty()", Op = "equals", Value = false }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Type_Function()
  {
    var responseJson = "{\"arr\":[1],\"obj\":{\"x\":1},\"str\":\"hi\",\"num\":1,\"bool\":true,\"nul\":null}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_type",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.arr.type()", Op = "equals", Value = "array" },
          new() { Key = "$.obj.type()", Op = "equals", Value = "object" },
          new() { Key = "$.str.type()", Op = "equals", Value = "string" },
          new() { Key = "$.num.type()", Op = "equals", Value = "number" },
          new() { Key = "$.bool.type()", Op = "equals", Value = "boolean" },
          new() { Key = "$.nul.type()", Op = "equals", Value = "null" }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Count_And_Size_Aliases()
  {
    var responseJson = "{\"items\":[1,2,3]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_count_size",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.items.count()", Op = "equals", Value = 3 },
          new() { Key = "$.items.size()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Values_And_Aggregates_Chaining()
  {
    var responseJson = "{\"obj\":{\"x\":1,\"y\":2,\"z\":3}}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_values_aggregates",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.obj.values().sum()", Op = "equals", Value = 6 },
          new() { Key = "$.obj.values().avg()", Op = "equals", Value = 2 },
          new() { Key = "$.obj.values().min()", Op = "equals", Value = 1 },
          new() { Key = "$.obj.values().max()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_ToNumber_On_StringArray_Then_Sum()
  {
    var responseJson = "{\"nums\":[\"1\",\"2\",\"3\"]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_to_number_sum",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.nums.to_number().sum()", Op = "equals", Value = 6 }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_ToString_On_Number()
  {
    var responseJson = "{\"val\":123}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_to_string_length",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.val.to_string().length()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_ToBoolean_On_Strings_And_Numbers()
  {
    var responseJson = "{\"t1\":\"true\",\"t2\":\"1\",\"f1\":\"false\",\"f2\":\"0\"}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_to_boolean",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.t1.to_boolean()", Op = "equals", Value = true },
          new() { Key = "$.t2.to_boolean()", Op = "equals", Value = true },
          new() { Key = "$.f1.to_boolean()", Op = "equals", Value = false },
          new() { Key = "$.f2.to_boolean()", Op = "equals", Value = false }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_String_Transforms_Trim_Lower_Upper()
  {
    var responseJson = "{\"s\":\" A b C \"}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_string_transforms",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.s.trim()", Op = "equals", Value = "A b C" },
          new() { Key = "$.s.lower()", Op = "equals", Value = " a b c " },
          new() { Key = "$.s.upper()", Op = "equals", Value = " A B C " }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }

  [Fact]
  public async Task ExpectValues_ShouldSupport_Aggregates_With_Mixed_Types()
  {
    var responseJson = "{\"nums\":[1,\"2\",null,\"bad\",3]}";
    var mockHandler = new Mock<HttpMessageHandler>();
    mockHandler.Protected()
      .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
      .ReturnsAsync(new HttpResponseMessage {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
      });

    var executor = new HttpTestExecutor(new HttpClient(mockHandler.Object));

    var test = new HttpTest {
      Name = "ev_agg_mixed",
      Method = "GET",
      Url = "https://api.example.com",
      Expect = new ExpectDefinition {
        Status = 200,
        Values = new List<ValueExpectation> {
          new() { Key = "$.nums.sum()", Op = "equals", Value = 6 },
          new() { Key = "$.nums.avg()", Op = "equals", Value = 2 },
          new() { Key = "$.nums.min()", Op = "equals", Value = 1 },
          new() { Key = "$.nums.max()", Op = "equals", Value = 3 }
        }
      }
    };

    var store = new VariableStore();
    var result = await executor.ExecuteTestAsync(test, store);
    Assert.True(result.Passed);
  }
}
