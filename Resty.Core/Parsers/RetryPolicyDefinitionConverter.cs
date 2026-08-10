namespace Resty.Core.Parsers;

using System.Globalization;
using Resty.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Parses retry as either the historical retry count or a structured policy.
/// </summary>
public sealed class RetryPolicyDefinitionConverter : IYamlTypeConverter
{
  public bool Accepts( Type type )
  {
    return type == typeof(RetryPolicyDefinition);
  }

  public object? ReadYaml( IParser parser, Type type, ObjectDeserializer deserializer )
  {
    if (parser.Current is Scalar scalar) {
      if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var retryCount)
        || retryCount < 0) {
        throw new YamlException("retry must be a non-negative integer or a structured retry policy.");
      }

      parser.MoveNext();
      return RetryPolicyDefinition.FromLegacyRetryCount(retryCount);
    }

    if (parser.Current is not MappingStart) {
      throw new YamlException("retry must be a non-negative integer or a structured retry policy.");
    }

    parser.MoveNext();
    var attempts = 1;
    object? delay = null;
    var backoff = RetryBackoffKind.Fixed;
    ExpectedStatusCodes? statuses = null;
    var expectationFailures = false;

    while (parser.Current is not MappingEnd) {
      if (parser.Current is not Scalar keyScalar) {
        throw new YamlException("retry policy keys must be strings.");
      }

      var key = keyScalar.Value;
      parser.MoveNext();

      switch (key.ToLowerInvariant()) {
        case "attempts":
          attempts = ReadInteger(parser, "retry.attempts");
          break;

        case "delay":
          delay = ReadScalar(parser, "retry.delay");
          break;

        case "backoff":
          var backoffValue = ReadScalar(parser, "retry.backoff");
          if (!string.Equals(backoffValue, "fixed", StringComparison.OrdinalIgnoreCase)) {
            throw new YamlException("retry.backoff currently supports only 'fixed'.");
          }
          backoff = RetryBackoffKind.Fixed;
          break;

        case "statuses":
          statuses = ExpectedStatusCodesConverter.ReadCodes(parser, "retry.statuses");
          break;

        case "expectation_failures":
        case "expectationfailures":
          var expectationValue = ReadScalar(parser, "retry.expectation_failures");
          if (!bool.TryParse(expectationValue, out expectationFailures)) {
            throw new YamlException("retry.expectation_failures must be true or false.");
          }
          break;

        default:
          throw new YamlException($"Unknown retry policy field '{key}'.");
      }
    }

    parser.MoveNext();

    if (attempts < 1) {
      throw new YamlException("retry.attempts must be at least 1.");
    }

    return new RetryPolicyDefinition {
      Attempts = attempts,
      Delay = delay,
      Backoff = backoff,
      Statuses = statuses ?? RetryPolicyDefinition.FromLegacyRetryCount(0).Statuses,
      ExpectationFailures = expectationFailures
    };
  }

  public void WriteYaml( IEmitter emitter, object? value, Type type, ObjectSerializer serializer )
  {
    if (value is not RetryPolicyDefinition policy) {
      emitter.Emit(new Scalar(null, null, string.Empty, ScalarStyle.Plain, true, false));
      return;
    }

    if (policy.Backoff == RetryBackoffKind.Exponential) {
      emitter.Emit(new Scalar(Math.Max(0, policy.Attempts - 1).ToString(CultureInfo.InvariantCulture)));
      return;
    }

    emitter.Emit(new MappingStart(null, null, false, MappingStyle.Block));
    WriteScalar(emitter, "attempts", policy.Attempts.ToString(CultureInfo.InvariantCulture));
    if (policy.Delay != null) {
      WriteScalar(emitter, "delay", policy.Delay.ToString() ?? string.Empty);
    }
    WriteScalar(emitter, "backoff", policy.Backoff.ToString().ToLowerInvariant());
    emitter.Emit(new Scalar("statuses"));
    new ExpectedStatusCodesConverter().WriteYaml(
      emitter,
      policy.Statuses,
      typeof(ExpectedStatusCodes),
      serializer);
    WriteScalar(emitter, "expectation_failures", policy.ExpectationFailures ? "true" : "false");
    emitter.Emit(new MappingEnd());
  }

  private static int ReadInteger( IParser parser, string fieldName )
  {
    var value = ReadScalar(parser, fieldName);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) {
      throw new YamlException($"{fieldName} must be an integer.");
    }
    return result;
  }

  private static string ReadScalar( IParser parser, string fieldName )
  {
    if (parser.Current is not Scalar scalar) {
      throw new YamlException($"{fieldName} must be a scalar value.");
    }
    parser.MoveNext();
    return scalar.Value;
  }

  private static void WriteScalar( IEmitter emitter, string key, string value )
  {
    emitter.Emit(new Scalar(key));
    emitter.Emit(new Scalar(value));
  }
}
