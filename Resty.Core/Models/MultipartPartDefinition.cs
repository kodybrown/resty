namespace Resty.Core.Models;

using YamlDotNet.Serialization;

/// <summary>
/// A named text field or file part in a multipart form request.
/// </summary>
public record MultipartPartDefinition
{
  public string Name { get; init; } = string.Empty;
  public string? Value { get; init; }
  public string? File { get; init; }

  [YamlMember(Alias = "content_type", ApplyNamingConventions = false)]
  public string? ContentType { get; init; }
}
