namespace Resty.Core.Helpers;

/// <summary>
/// Console color definitions for different types of output.
/// Uses System.ConsoleColor for reliable cross-platform support.
/// </summary>
public static class ConsoleColors
{
  /// <summary>
  /// Color for passed test status.
  /// </summary>
  public static System.ConsoleColor Passed => System.ConsoleColor.Green;

  /// <summary>
  /// Color for failed test status.
  /// </summary>
  public static System.ConsoleColor Failed => System.ConsoleColor.Red;

  /// <summary>
  /// Color for skipped test status.
  /// </summary>
  public static System.ConsoleColor Skipped => System.ConsoleColor.DarkGreen;

  /// <summary>
  /// Color for main headings (# Test Results).
  /// </summary>
  public static System.ConsoleColor Heading1 => System.ConsoleColor.Blue;

  /// <summary>
  /// Color for secondary headings (## Results by File).
  /// </summary>
  public static System.ConsoleColor Heading2 => System.ConsoleColor.DarkBlue;

  /// <summary>
  /// Color for file headings (### File: name.rest).
  /// </summary>
  public static System.ConsoleColor Heading3 => System.ConsoleColor.DarkCyan;

  /// <summary>
  /// Color for info messages and result summaries.
  /// </summary>
  public static System.ConsoleColor Info => System.ConsoleColor.Cyan;

  /// <summary>
  /// Color for warning messages.
  /// </summary>
  public static System.ConsoleColor Warning => System.ConsoleColor.Yellow;

  /// <summary>
  /// Color for success messages (ALL TESTS PASSED).
  /// </summary>
  public static System.ConsoleColor Success => System.ConsoleColor.Green;

  /// <summary>
  /// Color for error messages (TESTS FAILED).
  /// </summary>
  public static System.ConsoleColor Error => System.ConsoleColor.Red;

  /// <summary>
  /// Color for time durations.
  /// </summary>
  public static System.ConsoleColor TimeDuration => System.ConsoleColor.DarkGray;
}
