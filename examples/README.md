# Resty Examples

This folder contains example test files and output formats to demonstrate Resty's capabilities.

## Test Files

- **`api-tests.resty`** - Comprehensive example showing various HTTP methods, headers, response extraction, and variable usage
- **`sample-api.rest`** - Simple examples using the alternative `.rest` file extension

## Output Format Examples

Resty supports multiple output formats for different use cases:

### 1. Text Format (`results-example.txt`)

Plain text output optimized for human readability:
- Clean console-style formatting
- Test results organized by file
- Clear pass/fail indicators with timing
- Error messages for failed tests
- Summary statistics

**Best for:**
- Console output during development
- Simple test reports
- CI logs that need to be human-readable

```powershell
resty -o text
resty -o text -s results.txt
```

### 2. JSON Format (`results-example.json`)

Structured JSON output for programmatic processing:
- Complete test metadata
- Machine-readable format
- Environment information
- Nested structure with summary, results, and metadata
- Full request/response details when verbose

**Best for:**
- API integration
- Custom reporting tools
- Data analysis and metrics
- Integration with other systems

```powershell
resty -o json
resty -o json -s results.json
```

### 3. XML Format (`results-example.xml`)

JUnit-compatible XML format:
- Standard JUnit XML schema
- Test suites organized by file
- Compatible with CI/CD systems
- Detailed failure information
- System output for debugging

**Best for:**
- CI/CD integration (Jenkins, GitHub Actions, Azure DevOps)
- Test reporting dashboards
- Integration with testing frameworks
- Enterprise reporting systems

```powershell
resty -o xml
resty -o xml -s junit-results.xml
```

### 4. HTML Format (`results-example.html`)

Interactive web-based report:
- Modern, responsive design
- Interactive filtering (All/Passed/Failed)
- Collapsible test file sections
- Visual progress bars and statistics
- Mobile-friendly layout
- Professional dashboard view

**Features:**
- 📊 Visual dashboard with statistics
- 🎨 Modern gradient design
- 📱 Responsive mobile layout
- 🔍 Filter tests by status
- 📁 Expandable file sections
- ⚡ Fast client-side filtering
- 🎯 Detailed error information
- 📈 Progress visualization

**Best for:**
- Stakeholder presentations
- Team reviews and demos
- Detailed test analysis
- Archiving test results
- Sharing results with non-technical users

```powershell
resty -o html
resty -o html -s report.html
```

## Usage Examples

```powershell
# Run tests and display results in console
resty

# Run tests and save JSON results
resty -o json -s test-results.json

# Run tests with verbose output and save HTML report
resty -v -o html -s detailed-report.html

# Run specific tests and save XML for CI
resty -f api -o xml -s junit-results.xml

# Run tests from specific file with JSON output
resty api-tests.resty -o json
```

## File Extensions

Resty supports two file extensions:
- **`.resty`** - Primary extension (recommended)
- **`.rest`** - Alternative extension

Both work identically - choose based on your preference or team standards.

## Test Syntax

All test files use Markdown format with embedded YAML code blocks:

```markdown
# My API Tests

Description of your test suite.

```yaml
test: my-test-name
get: https://api.example.com/endpoint
headers:
  Authorization: Bearer $token
success:
  # Extract values using JSONPath syntax (note the $ prefix)
  user_id: $.user.id
  auth_token: $.result.token
``    <-- only using two backticks here to avoid ending the example markdown block
```

See the example files for complete syntax demonstrations.

## Response Extraction with JSONPath

Resty uses JSONPath syntax to extract values from JSON responses. The key points:

### Basic Syntax
- Always start with `$` (represents the JSON root)
- Use `.property` for object fields
- Use `[index]` for array elements
- Use `[*]` for all array elements

### Examples

```yaml
# For API response: {"result": {"token": "abc123", "user": {"id": 42}}}
test: login
post: /api/auth
success:
  auth_token: $.result.token    # Extracts "abc123"
  user_id: $.result.user.id     # Extracts 42
```

```yaml
# For API response: {"users": [{"name": "Alice"}, {"name": "Bob"}]}
test: get-users
get: /api/users
success:
  first_user: $.users[0].name   # Extracts "Alice"
  last_user: $.users[-1].name   # Extracts "Bob" (last element)
  all_names: $.users[*].name    # Extracts ["Alice", "Bob"]
```

### Common Patterns

- **Authentication tokens**: `$.result.token`, `$.access_token`
- **User data**: `$.user.id`, `$.profile.email`
- **API responses**: `$.data.items[0]`, `$.result.status`
- **Error handling**: `$.error.message`, `$.errors[0].description`

## Interactive HTML Report

The HTML report includes:
- **Dashboard** - Summary statistics and progress visualization
- **Filtering** - Show all tests, only passed, or only failed
- **File Organization** - Tests grouped by source file with expand/collapse
- **Test Details** - HTTP method, URL, duration, status code
- **Error Information** - Detailed error messages for failed tests
- **Extracted Variables** - Variables captured from responses
- **Metadata** - Environment and execution information

Open `results-example.html` in your browser to see the interactive report!
