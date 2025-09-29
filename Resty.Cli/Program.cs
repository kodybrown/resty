namespace Resty;

using System.Text;
using Resty.Core.Exceptions;
using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Output;

public enum OutputFormats { Text = 0, Markdown, Json, Xml, Html }

internal class Program : Resty.Helpers.ConsoleApplication
{
  /* Main() */

  public static async Task<int> Main( string[] arguments )
    => await new Program().Run(arguments);

  /* Command-line Arguments */

  private List<string> args = [];

  private bool OptDryRun { get; set; } = false;
  private bool OptList { get; set; } = false;
  private bool OptRunAll { get; set; } = false;
  private bool OptRecursive { get; set; } = true; // Default to recursive
  private List<string> OptPaths { get; set; } = [];
  private List<string> OptTests { get; set; } = [];
  private List<string> OptFilters { get; set; } = [];
  private OutputFormats OptOutputFormat { get; set; } = OutputFormats.Text;
  private bool OptMock { get; set; } = false; // Enable mocking engine globally
  private bool OptMockWasSet { get; set; } = false; // Track if set via CLI
  private string? OptSaveFile { get; set; } = null;
  private int OptParallelCount { get; set; } = 1;
  private int OptTestTimeoutSeconds { get; set; } = 30;
  private bool OptTestTimeoutWasSet { get; set; } = false; // Tracks if OptTestTimeoutSeconds was explicitly set by user
  private bool OptColor { get; set; } = true; // Default to true (enable colors)

  /* Program */

  public async Task<int> Run( string[] arguments )
  {
    Console.OutputEncoding = Encoding.UTF8;

    args.AddRange(arguments);

    var exit_code = ParseArguments(arguments);
    if (exit_code != 0) {
      // Ensure session lock is released on early exit
      return exit_code < 0 ? exit_code : 0;
    }

    // Allow RESTY_MOCK env var to set global mocking when CLI flag not provided
    var envMock = Environment.GetEnvironmentVariable("RESTY_MOCK");
    if (!OptMockWasSet && !string.IsNullOrWhiteSpace(envMock)) {
      var v = envMock.Trim().ToLowerInvariant();
      OptMock = v == "1" || v == "true" || v == "yes" || v == "on";
    }

    if (OptHelp) {
      ShowHelp(OptHelpTopic);
      return 0;
    } else if (OptVersionFull || OptVersion) {
      ShowVersion();
      return 0;
    }

    return await RunTestsAsync();
  }

  /* Command-line Arguments Parsing */

  private int ParseArguments( string[] arguments )
  {
    var (code, args) = base.ParseCommonArguments(arguments);
    if (code != 0) {
      return code < 0 ? code : 0;
    }

    var exit_code = 0;

    for (var i = 0; i < args.Length; i++) {
      var arg = args[i];
      var isFlag = false;

      while (arg.StartsWith('-') || arg.StartsWith('/')) {
        isFlag = true;
        arg = arg.Substring(1);
      }
      var argx = arg.ToLower();

      if (isFlag) {
        switch (argx) {
          case "dry-run": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptDryRun = found ? value : true; // Default to true if flag is present
            continue;
          }
          case "list" or "l": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptList = found ? value : true; // Default to true if flag is present
            continue;
          }
          case "all" or "a" or "run-all": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptRunAll = found ? value : true; // Default to true if flag is present
            continue;
          }

          case "path" or "p": {
            i = GetSubArgument(args, i, out var found, out var value, ignoreFlagSymbols: true);
            if (found && !string.IsNullOrEmpty(value)) {
              OptPaths.Add(value);
            }
            continue;
          }
          case "test" or "t": {
            i = GetSubArgument(args, i, out var found, out var value, ignoreFlagSymbols: true);
            if (found && !string.IsNullOrEmpty(value)) {
              OptTests.Add(value);
            }
            continue;
          }
          case "filter" or "f": {
            i = GetSubArgument(args, i, out var found, out var value, ignoreFlagSymbols: true);
            if (found && !string.IsNullOrEmpty(value)) {
              OptFilters.Add(value);
            }
            continue;
          }

          case "output" or "o": {
            i = GetSubArgument<string>(args, i, out var found, out var value);
            if (found && !string.IsNullOrEmpty(value)) {
              switch (value.ToLower()) {
                case "text" or "txt":
                  OptOutputFormat = OutputFormats.Text;
                  break;
                case "markdown" or "md":
                  OptOutputFormat = OutputFormats.Markdown;
                  break;
                case "json" or "jsn":
                  OptOutputFormat = OutputFormats.Json;
                  break;
                case "xml":
                  OptOutputFormat = OutputFormats.Xml;
                  break;
                case "html" or "htm":
                  OptOutputFormat = OutputFormats.Html;
                  break;
                default:
                  Console.WriteLine($"Unknown output format: {value}");
                  exit_code = -112;
                  break;
              }
            }
            continue;
          }

          case "save" or "s": {
            i = GetSubArgument<string>(args, i, out var found, out var value);
            if (found && !string.IsNullOrEmpty(value)) {
              OptSaveFile = value;
            }
            continue;
          }

          case "parallel": {
            i = GetSubArgument<int>(args, i, out var found, out var value);
            if (found) {
              OptParallelCount = value;
            }
            continue;
          }
          case "timeout": {
            OptTestTimeoutWasSet = true;
            i = GetSubArgument<int>(args, i, out var found, out var value);
            if (found) {
              OptTestTimeoutSeconds = value;
            }
            continue;
          }
          case "recursive" or "r": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptRecursive = found ? value : true; // Default to true if flag is present
            continue;
          }
          case "color" or "c": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptColor = found ? value : true; // Default to true if flag is present
            continue;
          }
          case "mock": {
            i = GetSubArgument<bool>(args, i, out var found, out var value);
            OptMock = found ? value : true; // Default true when present
            OptMockWasSet = true;
            continue;
          }
        }

        Console.WriteLine($"Unknown argument: {arg}");
        exit_code = -110;
        break;

      } else {
        // All arguments that don't start with - or / are handled here.
        if (File.Exists(arg)) {
          OptPaths.Add(arg);
          continue;
        }
        if (Directory.Exists(arg)) {
          OptPaths.Add(arg);
          continue;
        }

        Console.WriteLine($"Unknown command/flag: {arg}");
        exit_code = -111;
        break;
      }
    }

    return exit_code;
  }

  private void ShowHelp( string? topic = null, bool? includeExamples = null )
  {
    Console.WriteLine("Resty - REST API Testing Tool");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("    resty [OPTIONS] [PATHS...]");
    Console.WriteLine();
    Console.WriteLine("ARGUMENTS:");
    Console.WriteLine("    [PATHS...]    .resty/.rest files or directories to test (default: current directory)");
    Console.WriteLine();
    Console.WriteLine("OPTIONS:");
    Console.WriteLine("    -h, --help              Show this help message");
    Console.WriteLine("    -v                      Show version");
    Console.WriteLine("    --version               Show detailed version information");
    Console.WriteLine("    --verbose               Enable verbose output");
    Console.WriteLine();
    Console.WriteLine("TEST SELECTION:");
    Console.WriteLine("    -l, --list              List available tests without running them");
    Console.WriteLine("    -a, --all               Run all tests (default behavior)");
    Console.WriteLine("    -r, --recursive         Search subdirectories recursively (default: true)");
    Console.WriteLine("    -p, --path <PATH>       Specific file or directory path (can be used multiple times)");
    Console.WriteLine("    -t, --test <NAME>       Run specific test by exact name (can be used multiple times)");
    Console.WriteLine("    -f, --filter <PATTERN>  Run tests matching pattern (can be used multiple times)");
    Console.WriteLine();
    Console.WriteLine("OUTPUT:");
    Console.WriteLine("    -o, --output <FORMAT>   Output format: text (default), json, xml, html");
    Console.WriteLine("    -s, --save <FILE>       Save results to file");
    Console.WriteLine("    -c, --color <BOOL>      Enable colored console output (default: true)");
    Console.WriteLine();
    Console.WriteLine("VALIDATION:");
    Console.WriteLine("    --dry-run               Validate test files without executing");
    Console.WriteLine();
    Console.WriteLine("ADVANCED:");
    Console.WriteLine("    --parallel <N>          Number of parallel threads (future feature)");
    Console.WriteLine("    --timeout <SECONDS>     HTTP request timeout in seconds (default: 30)");
    Console.WriteLine("    --mock                  Enable mocking (try mocks first; fallback to network unless test has mock_only)");
    Console.WriteLine();
    Console.WriteLine("ENVIRONMENT:");
    Console.WriteLine("    RESTY_MOCK=true         Enables --mock globally unless overridden by the CLI flag");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("    resty                           # Run all tests in current directory (recursive)");
    Console.WriteLine("    resty -v                        # Run with verbose output");
    Console.WriteLine("    resty auth/                     # Run tests in auth folder (recursive)");
    Console.WriteLine("    resty auth/ --recursive false   # Run tests only in auth folder (non-recursive)");
    Console.WriteLine("    resty auth/auth.resty           # Run specific test file");
    Console.WriteLine("    resty -t login                  # Run test named 'login'");
    Console.WriteLine("    resty -f auth                   # Run tests containing 'auth'");
    Console.WriteLine("    resty -l                        # List all available tests");
    Console.WriteLine("    resty --dry-run                 # Validate without running");
    Console.WriteLine("    resty -o json -s results.json   # Save JSON results to file");
    Console.WriteLine("    resty --color false             # Disable colored output");
    Console.WriteLine();
    Console.WriteLine("EXIT CODES:");
    Console.WriteLine("    0    All tests passed");
    Console.WriteLine("    1    One or more tests failed");
    Console.WriteLine("    2    Internal error or invalid configuration");
    Console.WriteLine("    3    Missing test dependency");
    Console.WriteLine("    4    Circular test dependency detected");
  }

  private void ShowVersion()
  {
    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
    var version = assembly.GetName().Version?.ToString() ?? "1.0.0";

    if (OptVersionFull) {
      Console.WriteLine($"Resty v{version}");
      Console.WriteLine("Markdown-based REST API Testing Tool");
      Console.WriteLine();
      Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
      Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
      Console.WriteLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
    } else {
      Console.WriteLine($"Resty v{version}");
    }
  }

  /* Application Code */

  private async Task<int> RunTestsAsync()
  {
    try {
      // Step 1: Discover test files
      var testFiles = await DiscoverTestFilesAsync();
      if (testFiles.Count == 0) {
        Console.WriteLine("No test files found.");
        return 1;
      }

      // Step 2: Set up HTTP client and test runner
      using var httpClient = new HttpClient();
      httpClient.Timeout = TimeSpan.FromSeconds(OptTestTimeoutSeconds);

      var executor = new HttpTestExecutor(httpClient, OptMock);
      var runner = new TestSuiteRunner(executor, OptTestTimeoutSeconds, OptTestTimeoutWasSet);

      // Step 3: Handle list option
      if (OptList) {
        await ListTestsAsync(runner, testFiles);
        return 0;
      }

      // Step 4: Handle dry run option
      if (OptDryRun) {
        await DryRunAsync(runner, testFiles);
        return 0;
      }

      // Step 5: Execute tests
      var results = await ExecuteTestsAsync(runner, testFiles);

      // Step 6: Output results
      await OutputResultsAsync(results);

      // Step 7: Return appropriate exit code
      return results.HasFailures ? 1 : 0;
    } catch (MissingDependencyException mdx) {
      Console.WriteLine($"Error: {mdx.Message}");
      if (OptVerbose) {
        Console.WriteLine(mdx.ToString());
      }
      return 3;
    } catch (CircularDependencyException cdx) {
      Console.WriteLine($"Error: {cdx.Message}");
      if (OptVerbose) {
        Console.WriteLine(cdx.ToString());
      }
      return 4;
    } catch (Exception ex) {
      Console.WriteLine($"Error: {ex.Message}");
      if (OptVerbose) {
        Console.WriteLine(ex.ToString());
      }
      return 2;
    }
  }

  private Task<List<string>> DiscoverTestFilesAsync()
  {
    var testFiles = new List<string>();

    if (OptPaths.Count == 0) {
      // Default: current directory
      OptPaths.Add(Directory.GetCurrentDirectory());
    }

    foreach (var path in OptPaths) {
      if (File.Exists(path)) {
        if (path.EndsWith(".resty", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".rest", StringComparison.OrdinalIgnoreCase)) {
          testFiles.Add(Path.GetFullPath(path));
        }
      } else if (Directory.Exists(path)) {
        var searchOption = OptRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var restyFiles = Directory.GetFiles(path, "*.resty", searchOption);
        var restFiles = Directory.GetFiles(path, "*.rest", searchOption);
        testFiles.AddRange(restyFiles.Select(Path.GetFullPath));
        testFiles.AddRange(restFiles.Select(Path.GetFullPath));
      } else {
        Console.WriteLine($"Warning: Path not found: {path}");
      }
    }

    return Task.FromResult(testFiles);
  }

  private Task ListTestsAsync( TestSuiteRunner runner, List<string> testFiles )
  {
    Console.WriteLine("Available tests:");
    Console.WriteLine();

    var allTests = runner.ListAvailableTests(testFiles);
    var groupedTests = allTests.GroupBy(t => t.SourceFile);

    foreach (var group in groupedTests) {
      var fileName = Path.GetFileName(group.Key);
      Console.WriteLine($"📁 {fileName}");

      foreach (var test in group) {
        Console.WriteLine($"  ✓ {test.Name} ({test.Method} {test.Url})");
      }
      Console.WriteLine();
    }

    Console.WriteLine($"Total: {allTests.Count} tests in {groupedTests.Count()} files");
    return Task.CompletedTask;
  }

  private Task DryRunAsync( TestSuiteRunner runner, List<string> testFiles )
  {
    Console.WriteLine("🔍 Dry run - validating test files...");
    Console.WriteLine();

    var allTests = runner.ListAvailableTests(testFiles);
    var issues = new List<string>();

    foreach (var test in allTests) {
      // Basic validation
      if (string.IsNullOrEmpty(test.Name)) {
        issues.Add($"{Path.GetFileName(test.SourceFile)}:{test.SourceLine} - Test missing name");
      }
      if (string.IsNullOrEmpty(test.Url)) {
        issues.Add($"{Path.GetFileName(test.SourceFile)}:{test.SourceLine} - Test '{test.Name}' missing URL");
      }
    }

    if (issues.Count > 0) {
      Console.WriteLine("❌ Issues found:");
      foreach (var issue in issues) {
        Console.WriteLine($"  {issue}");
      }
    } else {
      Console.WriteLine($"✅ All {allTests.Count} tests are valid");
    }
    return Task.CompletedTask;
  }

  private async Task<TestRunSummary> ExecuteTestsAsync( TestSuiteRunner runner, List<string> testFiles )
  {
    TestRunSummary results;

    if (OptTests.Count > 0 || OptFilters.Count > 0) {
      // Run filtered tests
      var testNames = OptTests.Count > 0 ? OptTests : null;
      var patterns = OptFilters.Count > 0 ? OptFilters : null;
      results = await runner.RunFilteredTestsAsync(testFiles, testNames, patterns);
    } else {
      // Run all tests
      results = await runner.RunTestFilesAsync(testFiles);
    }

    return results;
  }

  private async Task OutputResultsAsync( TestRunSummary results )
  {
    var formatter = CreateOutputFormatter();

    formatter.WriteToConsole(results, OptVerbose, OptColor);

    if (!string.IsNullOrEmpty(OptSaveFile)) {
      await formatter.SaveToFileAsync(results, OptSaveFile);
      Console.WriteLine($"Results saved to: {OptSaveFile}");
    }
  }

  private IOutputFormatter CreateOutputFormatter()
  {
    return OptOutputFormat switch {
      OutputFormats.Markdown => new MarkdownOutputFormatter(),
      OutputFormats.Json => new JsonOutputFormatter(),
      OutputFormats.Xml => new XmlOutputFormatter(),
      OutputFormats.Html => new HtmlOutputFormatter(),
      _ => new ConsoleOutputFormatter(),
    };
  }
}
