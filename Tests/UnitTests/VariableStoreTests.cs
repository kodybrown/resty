namespace Resty.Tests;

using Resty.Core.Variables;

public class VariableStoreTests
{
  [Fact]
  public void VariableStore_ShouldMaintainCorrectPrecedence()
  {
    // Arrange
    var store = new VariableStore();

    // Act - Set variables in reverse precedence order
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["host"] = "included.example.com",
      ["timeout"] = 10
    });

    store.UpdateFileVariables(new Dictionary<string, object> {
      ["timeout"] = 30  // Override included value
    });

    store.SetCapturedVariables(new Dictionary<string, object> {
      ["token"] = "captured_token_123"
    });

    // Assert
    Assert.Equal("included.example.com", store.GetVariable("host"));
    Assert.Equal(30, store.GetVariable("timeout")); // File overrides included
    Assert.Equal("captured_token_123", store.GetVariable("token"));
  }

  [Fact]
  public void VariableStore_FileVariables_ShouldAccumulate()
  {
    // Arrange
    var store = new VariableStore();
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["host"] = "localhost",
      ["timeout"] = 10
    });

    // Act - Simulate processing YAML blocks in sequence
    // First block
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["timeout"] = 30
    });

    // Second block
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["timeout"] = 60,  // Override previous file value
      ["new_variable"] = "new_value"  // Add new variable
    });

    // Assert
    Assert.Equal("localhost", store.GetVariable("host"));  // From included
    Assert.Equal(60, store.GetVariable("timeout"));        // Latest file override
    Assert.Equal("new_value", store.GetVariable("new_variable"));  // New file variable
  }

  [Fact]
  public void ResolveVariables_ShouldHandleSimpleSubstitution()
  {
    // Arrange
    var store = new VariableStore();
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["host"] = "localhost:5114",
      ["username"] = "admin"
    });

    // Act
    var url = store.ResolveVariables("$host/api/auth");

    // Assert
    Assert.Equal("localhost:5114/api/auth", url);

    // Should throw for missing variable
    var ex = Assert.Throws<VariableNotFoundException>(() =>
        store.ResolveVariables("{\"user\": \"$username\", \"id\": \"$id\"}"));
    Assert.Contains("Variable 'id' not found", ex.Message);
    Assert.Contains("Available variables: host, username", ex.Message);
  }

  [Fact]
  public void ResolveVariables_ShouldHandleEnvironmentVariables()
  {
    // Arrange
    var store = new VariableStore();
    Environment.SetEnvironmentVariable("TEST_PASSWORD", "secret123");

    try {
      // Act
      var resolved = store.ResolveVariables("Password: $env:TEST_PASSWORD");

      // Assert
      Assert.Equal("Password: secret123", resolved);
    } finally {
      Environment.SetEnvironmentVariable("TEST_PASSWORD", null);
    }
  }

  [Fact]
  public void ResolveVariables_ShouldThrowForMissingEnvironmentVariable()
  {
    // Arrange
    var store = new VariableStore();

    // Act & Assert
    var ex = Assert.Throws<VariableNotFoundException>(() =>
        store.ResolveVariables("Password: $env:NONEXISTENT_VAR"));

    Assert.Contains("Environment variable 'NONEXISTENT_VAR' not found", ex.Message);
  }

  [Fact]
  public void VariableStore_ShouldSimulateYourScenario()
  {
    // Arrange - Your exact scenario
    var store = new VariableStore();

    // Initial state - load shared variables
    store.SetIncludedVariables(new Dictionary<string, object> {
      ["host"] = "localhost:5114"
    });

    // First YAML block with initial variables
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["timeout"] = 30
    });

    // Test A execution point
    Assert.Equal(30, store.GetVariable("timeout"));
    Assert.Null(store.GetVariable("id"));
    Assert.Null(store.GetVariable("new_variable"));

    var testAUrl = store.ResolveVariables("$host/api/testA");
    Assert.Equal("localhost:5114/api/testA", testAUrl);

    // Test B YAML block processing
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["timeout"] = 60  // Override previous value
    });

    // Test B execution point
    Assert.Equal(60, store.GetVariable("timeout"));  // Should be updated
    Assert.Null(store.GetVariable("id"));             // Still not exists
    Assert.Null(store.GetVariable("new_variable"));   // Still not exists

    // Test B completes and captures response
    store.SetCapturedVariables(new Dictionary<string, object> {
      ["id"] = "response_id_123",
      ["result"] = "success"
    });

    // Test C YAML block processing
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["new_variable"] = "new_value"
    });

    // Test C execution point
    Assert.Equal(60, store.GetVariable("timeout"));      // Still 60
    Assert.Equal("response_id_123", store.GetVariable("id"));  // From captured
    Assert.Equal("new_value", store.GetVariable("new_variable"));  // New file variable

    // Test C body resolution
    var testCBody = store.ResolveVariables("{\"id\": \"$id\", \"name\": \"$new_variable\"}");
    Assert.Equal("{\"id\": \"response_id_123\", \"name\": \"new_value\"}", testCBody);
  }

  [Fact]
  public void GetVariableSnapshot_ShouldShowSourcesCorrectly()
  {
    // Arrange
    var store = new VariableStore();
    store.SetIncludedVariables(new Dictionary<string, object> { ["var1"] = "included" });
    store.UpdateFileVariables(new Dictionary<string, object> { ["var2"] = "file" });
    store.SetCapturedVariables(new Dictionary<string, object> { ["var3"] = "captured" });

    // Act
    var snapshot = store.GetVariableSnapshot();

    // Assert
    Assert.Equal(3, snapshot.Count);
    Assert.Equal(("included", "Included"), snapshot["var1"]);
    Assert.Equal(("file", "File"), snapshot["var2"]);
    Assert.Equal(("captured", "Captured"), snapshot["var3"]);
  }

  [Fact]
  public void Clone_ShouldCreateIndependentCopy()
  {
    // Arrange
    var original = new VariableStore();
    original.SetIncludedVariables(new Dictionary<string, object> { ["var1"] = "original" });

    // Act
    var clone = original.Clone();
    clone.UpdateFileVariables(new Dictionary<string, object> { ["var2"] = "clone_only" });

    // Assert
    Assert.Equal("original", original.GetVariable("var1"));
    Assert.Null(original.GetVariable("var2"));

    Assert.Equal("original", clone.GetVariable("var1"));
    Assert.Equal("clone_only", clone.GetVariable("var2"));
  }
}
