namespace Resty.Core.Output;

using System.Text;
using Resty.Core.Models;

/// <summary>
/// Formats test results as HTML with modern styling and interactive features.
/// </summary>
public class HtmlOutputFormatter : IOutputFormatter
{
  public void WriteToConsole( TestRunSummary summary, bool verbose = false, bool useColors = true )
  {
    var html = GenerateHtml(summary, verbose);
    Console.WriteLine(html);
  }

  public async Task SaveToFileAsync( TestRunSummary summary, string filePath, bool verbose = false )
  {
    var html = GenerateHtml(summary, verbose: true); // Always include full details when saving
    await File.WriteAllTextAsync(filePath, html, Encoding.UTF8);
  }

  private string GenerateHtml( TestRunSummary summary, bool verbose )
  {
    var html = new StringBuilder();

    html.AppendLine("<!DOCTYPE html>");
    html.AppendLine("<html lang=\"en\">");
    html.AppendLine("<head>");
    html.AppendLine("    <meta charset=\"UTF-8\">");
    html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
    html.AppendLine("    <title>Resty Test Results</title>");
    html.AppendLine("    <style>");
    html.AppendLine(GetCss());
    html.AppendLine("    </style>");
    html.AppendLine("</head>");
    html.AppendLine("<body>");

    // Header
    html.AppendLine("    <header>");
    html.AppendLine("        <h1>🚀 Resty Test Results</h1>");
    html.AppendLine($"        <div class=\"timestamp\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");
    html.AppendLine("    </header>");

    // Summary Dashboard
    html.AppendLine("    <main>");
    html.AppendLine(GenerateSummaryDashboard(summary));

    // Test Results
    html.AppendLine(GenerateTestResults(summary, verbose));

    // Metadata
    html.AppendLine(GenerateMetadata(summary));

    html.AppendLine("    </main>");
    html.AppendLine("    <script>");
    html.AppendLine(GetJavaScript());
    html.AppendLine("    </script>");
    html.AppendLine("</body>");
    html.AppendLine("</html>");

    return html.ToString();
  }

  private string GenerateSummaryDashboard( TestRunSummary summary )
  {
    var statusClass = summary.HasFailures ? "failed" : "passed";
    var statusText = summary.HasFailures ? "FAILED" : "PASSED";

    var html = new StringBuilder();
    html.AppendLine("        <section class=\"dashboard\">");
    html.AppendLine($"            <div class=\"summary-card {statusClass}\">");
    html.AppendLine($"                <div class=\"status-badge {statusClass}\">{statusText}</div>");
    html.AppendLine($"                <div class=\"summary-stats\">");
    html.AppendLine($"                    <div class=\"stat\">");
    html.AppendLine($"                        <span class=\"stat-value\">{summary.TotalTests}</span>");
    html.AppendLine($"                        <span class=\"stat-label\">Total Tests</span>");
    html.AppendLine($"                    </div>");
    html.AppendLine($"                    <div class=\"stat passed\">");
    html.AppendLine($"                        <span class=\"stat-value\">{summary.PassedTests}</span>");
    html.AppendLine($"                        <span class=\"stat-label\">Passed</span>");
    html.AppendLine($"                    </div>");
    html.AppendLine($"                    <div class=\"stat failed\">");
    html.AppendLine($"                        <span class=\"stat-value\">{summary.FailedTests}</span>");
    html.AppendLine($"                        <span class=\"stat-label\">Failed</span>");
    html.AppendLine($"                    </div>");
    html.AppendLine($"                    <div class=\"stat\">");
    html.AppendLine($"                        <span class=\"stat-value\">{summary.PassRate:F1}%</span>");
    html.AppendLine($"                        <span class=\"stat-label\">Pass Rate</span>");
    html.AppendLine($"                    </div>");
    html.AppendLine($"                    <div class=\"stat\">");
    html.AppendLine($"                        <span class=\"stat-value\">{summary.TotalDuration.TotalSeconds:F1}s</span>");
    html.AppendLine($"                        <span class=\"stat-label\">Duration</span>");
    html.AppendLine($"                    </div>");
    html.AppendLine($"                </div>");
    html.AppendLine($"            </div>");

    // Progress bar
    var passPercent = summary.TotalTests > 0 ? (summary.PassedTests * 100.0 / summary.TotalTests) : 0;
    html.AppendLine($"            <div class=\"progress-container\">");
    html.AppendLine($"                <div class=\"progress-bar\">");
    html.AppendLine($"                    <div class=\"progress-fill\" style=\"width: {passPercent:F1}%\"></div>");
    html.AppendLine($"                </div>");
    html.AppendLine($"            </div>");
    html.AppendLine("        </section>");

    return html.ToString();
  }

  private string GenerateTestResults( TestRunSummary summary, bool verbose )
  {
    var html = new StringBuilder();
    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    html.AppendLine("        <section class=\"test-results\">");
    html.AppendLine("            <h2>Test Results</h2>");

    // Filter buttons
    html.AppendLine("            <div class=\"filters\">");
    html.AppendLine("                <button class=\"filter-btn active\" onclick=\"filterTests('all')\">All</button>");
    html.AppendLine("                <button class=\"filter-btn\" onclick=\"filterTests('passed')\">Passed</button>");
    html.AppendLine("                <button class=\"filter-btn\" onclick=\"filterTests('failed')\">Failed</button>");
    html.AppendLine("            </div>");

    foreach (var fileGroup in resultsByFile) {
      var fileName = Path.GetFileName(fileGroup.Key);
      var filePassCount = fileGroup.Count(r => r.Status == TestStatus.Passed);
      var fileFailCount = fileGroup.Count(r => r.Status == TestStatus.Failed);

      html.AppendLine($"            <div class=\"test-file\">");
      html.AppendLine($"                <div class=\"file-header\" onclick=\"toggleFile('{fileName}')\">");
      html.AppendLine($"                    <span class=\"file-icon\">📁</span>");
      html.AppendLine($"                    <span class=\"file-name\">{fileName}</span>");
      html.AppendLine($"                    <span class=\"file-stats\">");
      html.AppendLine($"                        <span class=\"passed\">{filePassCount} passed</span>");
      if (fileFailCount > 0) {
        html.AppendLine($"                        <span class=\"failed\">{fileFailCount} failed</span>");
      }
      html.AppendLine($"                    </span>");
      html.AppendLine($"                    <span class=\"expand-icon\">▼</span>");
      html.AppendLine($"                </div>");
      html.AppendLine($"                <div class=\"file-path\">{fileGroup.Key}</div>");
      html.AppendLine($"                <div class=\"test-list\" id=\"{fileName}-tests\">");

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        var statusClass = result.Status.ToString().ToLower();
        var statusIcon = GetStatusIcon(result.Status);
        var testUrl = result.RequestInfo?.Url ?? result.Test.Url;

        html.AppendLine($"                    <div class=\"test-item {statusClass}\" data-status=\"{statusClass}\">");
        html.AppendLine($"                        <div class=\"test-header\">");
        html.AppendLine($"                            <span class=\"test-icon\">{statusIcon}</span>");
        html.AppendLine($"                            <span class=\"test-name\">{EscapeHtml(result.Test.Name)}</span>");
        html.AppendLine($"                            <span class=\"test-method\">{result.Test.Method}</span>");
        html.AppendLine($"                            <span class=\"test-duration\">{result.Duration.TotalSeconds:F3}s</span>");
        html.AppendLine($"                        </div>");
        html.AppendLine($"                        <div class=\"test-url\">{EscapeHtml(testUrl)}</div>");

        if (result.Status == TestStatus.Failed && !string.IsNullOrEmpty(result.ErrorMessage)) {
          html.AppendLine($"                        <div class=\"test-error\">");
          html.AppendLine($"                            <strong>Error:</strong> {EscapeHtml(result.ErrorMessage)}");
          html.AppendLine($"                        </div>");

          // Show available variables for debugging failed tests
          if (result.VariableSnapshot?.Count > 0) {
            html.AppendLine($"                        <div class=\"variable-snapshot\">");
            html.AppendLine($"                            <strong>Available Variables:</strong>");
            html.AppendLine($"                            <ul>");
            foreach (var (name, (value, source)) in result.VariableSnapshot.OrderBy(kvp => kvp.Key)) {
              html.AppendLine($"                                <li><code>{EscapeHtml(name)}</code>: {EscapeHtml(value?.ToString() ?? "null")} <em>(from {EscapeHtml(source)})</em></li>");
            }
            html.AppendLine($"                            </ul>");
            html.AppendLine($"                        </div>");
          }
        }

        if (result.StatusCode.HasValue) {
          var statusCodeClass = ((int)result.StatusCode.Value >= 200 && (int)result.StatusCode.Value < 300) ? "success" : "error";
          html.AppendLine($"                        <div class=\"status-code {statusCodeClass}\">");
          html.AppendLine($"                            HTTP {(int)result.StatusCode.Value} {result.StatusCode.Value}");
          html.AppendLine($"                        </div>");
        }

        if (result.ExtractedVariables?.Count > 0) {
          html.AppendLine($"                        <div class=\"extracted-vars\">");
          html.AppendLine($"                            <strong>Extracted Variables:</strong>");
          html.AppendLine($"                            <ul>");
          foreach (var variable in result.ExtractedVariables) {
            html.AppendLine($"                                <li><code>{EscapeHtml(variable.Key)}</code>: {EscapeHtml(variable.Value?.ToString() ?? "null")}</li>");
          }
          html.AppendLine($"                            </ul>");
          html.AppendLine($"                        </div>");
        }

        if (verbose && result.RequestInfo != null && !string.IsNullOrEmpty(result.RequestInfo.Body)) {
          html.AppendLine($"                        <div class=\"request-body\">");
          html.AppendLine($"                            <strong>Request Body:</strong>");
          html.AppendLine($"                            <pre><code>{EscapeHtml(result.RequestInfo.Body)}</code></pre>");
          html.AppendLine($"                        </div>");
        }

        html.AppendLine($"                    </div>");
      }

      html.AppendLine($"                </div>");
      html.AppendLine($"            </div>");
    }

    html.AppendLine("        </section>");
    return html.ToString();
  }

  private string GenerateMetadata( TestRunSummary summary )
  {
    var html = new StringBuilder();
    html.AppendLine("        <section class=\"metadata\">");
    html.AppendLine("            <h3>Test Run Information</h3>");
    html.AppendLine("            <div class=\"metadata-grid\">");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">Tool:</span> Resty v1.0.0");
    html.AppendLine($"                </div>");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">Start Time:</span> {summary.StartTime:yyyy-MM-dd HH:mm:ss} UTC");
    html.AppendLine($"                </div>");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">End Time:</span> {summary.EndTime:yyyy-MM-dd HH:mm:ss} UTC");
    html.AppendLine($"                </div>");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">Runtime:</span> {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
    html.AppendLine($"                </div>");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">OS:</span> {Environment.OSVersion}");
    html.AppendLine($"                </div>");
    html.AppendLine($"                <div class=\"metadata-item\">");
    html.AppendLine($"                    <span class=\"label\">Machine:</span> {Environment.MachineName}");
    html.AppendLine($"                </div>");
    html.AppendLine("            </div>");
    html.AppendLine("        </section>");
    return html.ToString();
  }

  private static string GetStatusIcon( TestStatus status )
  {
    return status switch {
      TestStatus.Passed => "✅",
      TestStatus.Failed => "❌",
      TestStatus.Skipped => "⏭️",
      _ => "❓"
    };
  }

  private static string EscapeHtml( string? text )
  {
    if (string.IsNullOrEmpty(text)) {
      return string.Empty;
    }

    return text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");
  }

  private static string GetCss()
  {
    return @"
        * {
            box-sizing: border-box;
        }

        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            margin: 0;
            padding: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            color: #333;
        }

        header {
            background: rgba(255, 255, 255, 0.95);
            backdrop-filter: blur(10px);
            padding: 2rem;
            text-align: center;
            box-shadow: 0 2px 20px rgba(0, 0, 0, 0.1);
        }

        header h1 {
            margin: 0;
            font-size: 2.5rem;
            font-weight: 300;
            color: #333;
        }

        .timestamp {
            color: #666;
            font-size: 0.9rem;
            margin-top: 0.5rem;
        }

        main {
            max-width: 1200px;
            margin: 0 auto;
            padding: 2rem;
        }

        .dashboard {
            margin-bottom: 2rem;
        }

        .summary-card {
            background: white;
            border-radius: 12px;
            padding: 2rem;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
            margin-bottom: 1rem;
        }

        .status-badge {
            display: inline-block;
            padding: 0.5rem 1rem;
            border-radius: 25px;
            font-weight: bold;
            font-size: 0.9rem;
            margin-bottom: 1.5rem;
        }

        .status-badge.passed {
            background: #d4edda;
            color: #155724;
        }

        .status-badge.failed {
            background: #f8d7da;
            color: #721c24;
        }

        .summary-stats {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 1.5rem;
        }

        .stat {
            text-align: center;
        }

        .stat-value {
            display: block;
            font-size: 2.5rem;
            font-weight: bold;
            margin-bottom: 0.5rem;
        }

        .stat-label {
            color: #666;
            font-size: 0.9rem;
        }

        .stat.passed .stat-value { color: #28a745; }
        .stat.failed .stat-value { color: #dc3545; }

        .progress-container {
            background: white;
            border-radius: 12px;
            padding: 1rem;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.05);
        }

        .progress-bar {
            background: #e9ecef;
            border-radius: 10px;
            height: 20px;
            overflow: hidden;
        }

        .progress-fill {
            background: linear-gradient(90deg, #28a745, #20c997);
            height: 100%;
            border-radius: 10px;
            transition: width 0.3s ease;
        }

        .test-results {
            background: white;
            border-radius: 12px;
            padding: 2rem;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
            margin-bottom: 2rem;
        }

        .test-results h2 {
            margin-top: 0;
            color: #333;
        }

        .filters {
            margin-bottom: 2rem;
            display: flex;
            gap: 0.5rem;
        }

        .filter-btn {
            padding: 0.5rem 1rem;
            border: 2px solid #dee2e6;
            background: white;
            border-radius: 6px;
            cursor: pointer;
            transition: all 0.2s;
        }

        .filter-btn:hover {
            background: #f8f9fa;
        }

        .filter-btn.active {
            background: #007bff;
            color: white;
            border-color: #007bff;
        }

        .test-file {
            border: 1px solid #dee2e6;
            border-radius: 8px;
            margin-bottom: 1rem;
            overflow: hidden;
        }

        .file-header {
            background: #f8f9fa;
            padding: 1rem;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 1rem;
            transition: background-color 0.2s;
        }

        .file-header:hover {
            background: #e9ecef;
        }

        .file-icon {
            font-size: 1.2rem;
        }

        .file-name {
            font-weight: bold;
            flex: 1;
        }

        .file-stats {
            display: flex;
            gap: 1rem;
            font-size: 0.9rem;
        }

        .file-stats .passed { color: #28a745; }
        .file-stats .failed { color: #dc3545; }

        .expand-icon {
            transition: transform 0.2s;
        }

        .file-header.collapsed .expand-icon {
            transform: rotate(-90deg);
        }

        .file-path {
            background: #f8f9fa;
            padding: 0.5rem 1rem;
            font-size: 0.85rem;
            color: #666;
            font-family: monospace;
        }

        .test-list {
            display: block;
            transition: max-height 0.3s ease;
        }

        .test-list.collapsed {
            display: none;
        }

        .test-item {
            padding: 1rem;
            border-top: 1px solid #dee2e6;
        }

        .test-item.hidden {
            display: none;
        }

        .test-header {
            display: flex;
            align-items: center;
            gap: 1rem;
            margin-bottom: 0.5rem;
        }

        .test-icon {
            font-size: 1.1rem;
        }

        .test-name {
            font-weight: bold;
            flex: 1;
        }

        .test-method {
            background: #e9ecef;
            padding: 0.25rem 0.5rem;
            border-radius: 4px;
            font-size: 0.8rem;
            font-family: monospace;
        }

        .test-duration {
            color: #666;
            font-size: 0.9rem;
        }

        .test-url {
            color: #666;
            font-size: 0.9rem;
            font-family: monospace;
            margin-bottom: 0.5rem;
            word-break: break-all;
        }

        .test-error {
            background: #f8d7da;
            color: #721c24;
            padding: 0.75rem;
            border-radius: 6px;
            margin: 0.5rem 0;
            font-size: 0.9rem;
        }

        .status-code {
            display: inline-block;
            padding: 0.25rem 0.5rem;
            border-radius: 4px;
            font-size: 0.8rem;
            font-family: monospace;
            margin: 0.5rem 0;
        }

        .status-code.success {
            background: #d4edda;
            color: #155724;
        }

        .status-code.error {
            background: #f8d7da;
            color: #721c24;
        }

        .extracted-vars {
            background: #f8f9fa;
            padding: 0.75rem;
            border-radius: 6px;
            margin: 0.5rem 0;
            font-size: 0.9rem;
        }

        .extracted-vars ul {
            margin: 0.5rem 0 0 0;
            padding-left: 1.5rem;
        }

        .extracted-vars code {
            background: #e9ecef;
            padding: 0.125rem 0.25rem;
            border-radius: 3px;
            font-size: 0.85rem;
        }

        .request-body {
            margin: 0.5rem 0;
        }

        .request-body pre {
            background: #f8f9fa;
            padding: 0.75rem;
            border-radius: 6px;
            overflow-x: auto;
            font-size: 0.85rem;
            margin: 0.5rem 0;
        }

        .variable-snapshot {
            background: #f8f9fa;
            padding: 0.75rem;
            border-radius: 6px;
            margin: 0.5rem 0;
            font-size: 0.9rem;
        }

        .variable-snapshot ul {
            margin: 0.5rem 0 0 0;
            padding-left: 1.5rem;
        }

        .variable-snapshot code {
            background: #e9ecef;
            padding: 0.125rem 0.25rem;
            border-radius: 3px;
            font-size: 0.85rem;
        }

        .variable-snapshot em {
            color: #666;
            font-size: 0.85rem;
        }

        .metadata {
            background: white;
            border-radius: 12px;
            padding: 2rem;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
        }

        .metadata h3 {
            margin-top: 0;
            color: #333;
        }

        .metadata-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 1rem;
        }

        .metadata-item {
            padding: 0.5rem 0;
        }

        .metadata-item .label {
            font-weight: bold;
            color: #666;
        }

        @media (max-width: 768px) {
            main {
                padding: 1rem;
            }

            .summary-stats {
                grid-template-columns: repeat(2, 1fr);
            }

            .test-header {
                flex-direction: column;
                align-items: stretch;
                gap: 0.5rem;
            }

            .file-stats {
                flex-direction: column;
                gap: 0.25rem;
            }
        }";
  }

  private static string GetJavaScript()
  {
    return @"
        function filterTests(status) {
            // Update active filter button
            document.querySelectorAll('.filter-btn').forEach(btn => {
                btn.classList.remove('active');
            });
            event.target.classList.add('active');

            // Show/hide test items
            document.querySelectorAll('.test-item').forEach(item => {
                if (status === 'all') {
                    item.classList.remove('hidden');
                } else {
                    if (item.dataset.status === status) {
                        item.classList.remove('hidden');
                    } else {
                        item.classList.add('hidden');
                    }
                }
            });
        }

        function toggleFile(fileName) {
            const header = event.currentTarget;
            const testList = document.getElementById(fileName + '-tests');

            if (testList.classList.contains('collapsed')) {
                testList.classList.remove('collapsed');
                header.classList.remove('collapsed');
            } else {
                testList.classList.add('collapsed');
                header.classList.add('collapsed');
            }
        }

        // Initialize - expand all files by default
        document.addEventListener('DOMContentLoaded', function() {
            // Add click handlers and expand all files
            document.querySelectorAll('.file-header').forEach(header => {
                header.classList.remove('collapsed');
            });
        });";
  }
}
