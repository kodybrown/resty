namespace Resty.Core.Models;

/// <summary>
/// One or more acceptable HTTP response status codes.
/// </summary>
public sealed class ExpectedStatusCodes
{
  public ExpectedStatusCodes( IEnumerable<int> codes )
  {
    Codes = codes
      .Distinct()
      .ToList()
      .AsReadOnly();

    if (Codes.Count == 0) {
      throw new ArgumentException("At least one expected status code is required.", nameof(codes));
    }
  }

  /// <summary>
  /// Acceptable response status codes in declaration order.
  /// </summary>
  public IReadOnlyList<int> Codes { get; }

  public static implicit operator ExpectedStatusCodes( int code )
  {
    return new ExpectedStatusCodes(new[] { code });
  }

  public static implicit operator ExpectedStatusCodes( int[] codes )
  {
    return new ExpectedStatusCodes(codes);
  }

  public static implicit operator ExpectedStatusCodes( List<int> codes )
  {
    return new ExpectedStatusCodes(codes);
  }
}
