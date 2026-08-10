namespace Resty.Tests;

using System.Globalization;
using System.Text.RegularExpressions;
using Resty.Core.Variables;

public class ExpressionVariableTests
{
  private static readonly DateTimeOffset FixedUtcNow = new(2026, 8, 10, 14, 37, 42, TimeSpan.Zero);

  [Fact]
  public void Expressions_ShouldCalculatePreviousCompletedUtcHour()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["hour_start"] = "${{ hour_end - hours(1) }}",
      ["hour_end"] = "${{ start_of_hour(utc_now()) }}"
    });

    Assert.Equal(
      new DateTimeOffset(2026, 8, 10, 14, 0, 0, TimeSpan.Zero),
      store.GetVariable("hour_end"));
    Assert.Equal(
      new DateTimeOffset(2026, 8, 10, 13, 0, 0, TimeSpan.Zero),
      store.GetVariable("hour_start"));
  }

  [Fact]
  public void Expressions_ShouldSupportDateArithmeticAndUnixConversions()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["window_start"] = "${{ start_of_hour(utc_now()) - hours(1) }}",
      ["read_time"] = "${{ window_start + minutes(20) }}",
      ["epoch_seconds"] = "${{ unix_seconds(read_time) }}",
      ["epoch_milliseconds"] = "${{ unix_milliseconds(read_time) }}",
      ["window_duration"] = "${{ hours(1) - minutes(15) }}"
    });

    var readTime = new DateTimeOffset(2026, 8, 10, 13, 20, 0, TimeSpan.Zero);
    Assert.Equal(readTime, store.GetVariable("read_time"));
    Assert.Equal(readTime.ToUnixTimeSeconds(), store.GetVariable("epoch_seconds"));
    Assert.Equal(readTime.ToUnixTimeMilliseconds(), store.GetVariable("epoch_milliseconds"));
    Assert.Equal(TimeSpan.FromMinutes(45), store.GetVariable("window_duration"));
  }

  [Fact]
  public void Expressions_ShouldHonorArithmeticPrecedenceAndParentheses()
  {
    var store = CreateStore();

    Assert.Equal(14L, store.EvaluateExpression("2 + 3 * 4"));
    Assert.Equal(20L, store.EvaluateExpression("(2 + 3) * 4"));
    Assert.Equal(2L, store.EvaluateExpression("17 % 5"));
    Assert.Equal(5m, store.EvaluateExpression("10 / 2"));
  }

  [Fact]
  public void Expressions_ShouldSupportNumericAndBooleanOperations()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["initial_value"] = 1000.0,
      ["next_value"] = "${{ initial_value + 1.25 }}",
      ["scaled_value"] = "${{ next_value * 1000 }}"
    });

    Assert.Equal(1001.25m, store.GetVariable("next_value"));
    Assert.Equal(1001250.00m, store.GetVariable("scaled_value"));
    Assert.Equal(true, store.EvaluateExpression("next_value > 1000 && next_value < 1002"));
    Assert.Equal(true, store.EvaluateExpression("not false and (1 == 1)"));
  }

  [Fact]
  public void Expressions_ShouldResolveVariablesRegardlessOfDeclarationOrder()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["third"] = "${{ second + 1 }}",
      ["second"] = "${{ $first + 1 }}",
      ["first"] = 1
    });

    Assert.Equal(3L, store.GetVariable("third"));
  }

  [Fact]
  public void Expressions_ShouldReportMissingReferences()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["value"] = "${{ missing + 1 }}"
    });

    var exception = Assert.Throws<ExpressionEvaluationException>(() => store.GetVariable("value"));

    Assert.Contains("Variable 'missing' was not found", exception.Message);
  }

  [Fact]
  public void Expressions_ShouldReportReferenceCycles()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["first"] = "${{ second + 1 }}",
      ["second"] = "${{ third + 1 }}",
      ["third"] = "${{ first + 1 }}"
    });

    var exception = Assert.Throws<VariableResolutionException>(() => store.GetVariable("first"));

    Assert.Contains("first -> second -> third -> first", exception.Message);
  }

  [Fact]
  public void ExactSubstitutions_ShouldPreserveTypes()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["count"] = 42,
      ["enabled"] = true,
      ["read_time"] = "${{ start_of_hour(utc_now()) }}",
      ["delay"] = "${{ milliseconds(500) }}"
    });

    Assert.IsType<int>(store.ResolveValue("$count"));
    Assert.IsType<bool>(store.ResolveValue("$enabled"));
    Assert.IsType<DateTimeOffset>(store.ResolveValue("${{ read_time }}"));
    Assert.IsType<TimeSpan>(store.ResolveValue("$delay"));
  }

  [Fact]
  public void StringInterpolation_ShouldUseInvariantFormatting()
  {
    var originalCulture = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    try {
      var store = CreateStore();
      store.UpdateFileVariables(new Dictionary<string, object> {
        ["value"] = 12.5,
        ["enabled"] = true,
        ["read_time"] = "${{ start_of_hour(utc_now()) }}"
      });

      Assert.Equal("value=12.5&enabled=true", store.ResolveVariables("value=$value&enabled=$enabled"));
      Assert.Equal(
        "https://api.example.com?start=2026-08-10T14:00:00.0000000+00:00",
        store.ResolveVariables("https://api.example.com?start=$read_time"));
      Assert.Equal("20260810-140000", store.ResolveVariables("${{ format(read_time, 'yyyyMMdd-HHmmss') }}"));
    } finally {
      CultureInfo.CurrentCulture = originalCulture;
    }
  }

  [Fact]
  public void GeneratorFunctions_ShouldReturnBoundedTypedValues()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["id"] = "${{ uuid() }}",
      ["dev_eui"] = "${{ random_hex(16) }}",
      ["number"] = "${{ random_int(10, 20) }}"
    });

    Assert.IsType<Guid>(store.GetVariable("id"));
    Assert.Matches(new Regex("^[0-9A-F]{16}$"), Assert.IsType<string>(store.GetVariable("dev_eui")));
    Assert.InRange(Assert.IsType<int>(store.GetVariable("number")), 10, 19);
  }

  [Fact]
  public void ComputedGenerators_ShouldRemainStableAfterUnrelatedCapture()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["id"] = "${{ uuid() }}",
      ["dev_eui"] = "${{ random_hex(16) }}"
    });
    var firstId = store.GetVariable("id");
    var firstDevEui = store.GetVariable("dev_eui");

    store.SetCapturedVariables(new Dictionary<string, object> {
      ["response_id"] = 42
    });

    Assert.Equal(firstId, store.GetVariable("id"));
    Assert.Equal(firstDevEui, store.GetVariable("dev_eui"));
  }

  [Fact]
  public void ComputedVariables_ShouldReevaluateWhenADependencyIsCaptured()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["base_value"] = 1,
      ["derived_value"] = "${{ base_value + 1 }}"
    });
    Assert.Equal(2L, store.GetVariable("derived_value"));

    store.SetCapturedVariables(new Dictionary<string, object> {
      ["base_value"] = 5
    });

    Assert.Equal(6L, store.GetVariable("derived_value"));
  }

  [Fact]
  public void LegacySubstitution_ShouldRemainCompatible()
  {
    var store = CreateStore();
    store.UpdateFileVariables(new Dictionary<string, object> {
      ["host"] = "https://api.example.com"
    });
    Environment.SetEnvironmentVariable("RESTY_EXPRESSION_TEST_TOKEN", "secret");

    try {
      Assert.Equal("https://api.example.com/users", store.ResolveVariables("$host/users"));
      Assert.Equal("Bearer secret", store.ResolveVariables("Bearer $env:RESTY_EXPRESSION_TEST_TOKEN"));
      Assert.Matches("^[0-9]{5}$", store.ResolveVariables("$RandomNumber"));
    } finally {
      Environment.SetEnvironmentVariable("RESTY_EXPRESSION_TEST_TOKEN", null);
    }
  }

  private static VariableStore CreateStore()
  {
    return new VariableStore(new FixedTimeProvider(FixedUtcNow));
  }

  private sealed class FixedTimeProvider : TimeProvider
  {
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider( DateTimeOffset utcNow )
    {
      _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
      return _utcNow;
    }
  }
}
