# Resty 🚀

A powerful, modern REST API testing tool that lets you write tests in human-readable format using `.resty` (or `.rest`) files with advanced features like variable substitution, response capture, and multiple output formats.

A .resty file is just a Markdown file with YAML code blocks defining HTTP requests and tests.

## VSCode Extension

We also have a VSCode extension for Resty.
Use the [Resty.VSCode](https://github.com/kodybrown/resty-vscode) extension for the best experience!

## Features

✅ **Human-Readable Test Format** - Write tests in markdown with embedded YAML
✅ **Variable System** - Support for file variables, environment variables, and response capture
✅ **Multiple Output Formats** - Console, JSON, JUnit XML, and Interactive HTML reports
✅ **Test Organization** - Group tests by files and directories
✅ **Filtering & Selection** - Run specific tests, patterns, or entire suites
✅ **Response Capture** - Extract values from responses for use in subsequent tests
✅ **Shared Configuration** - Include common variables and settings across test files
✅ **Professional Output** - Beautiful console output with colors and detailed reporting

## Quick Start

### Installation

#### Option 1: Quick Build (Recommended)

Use the provided build scripts for easy compilation:

```powershell
# Windows (PowerShell)
./publish.ps1                    # Auto-detects platform, copies to ~/Bin

# Linux/macOS (Bash)
./publish.sh                     # Auto-detects platform, copies to ~/bin

# Windows (Batch)
publish.bat                      # Simple Windows build
```

#### Option 2: Manual Build

```powershell
# Clone and build from source
git clone <repository>
cd Resty
dotnet build

# Run from source
dotnet run --project Resty.Cli -- --help

# Or build single-file executable
dotnet publish Resty.Cli/Resty.Cli.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true -o ./publish
```

> 💡 **Tip**: The build scripts automatically detect your platform and create optimized, self-contained executables. See [BUILD.md](BUILD.md) for advanced build options.

### Basic Usage

Remember that a .resty file is just a Markdown file with YAML code blocks.
If there is a YAML code block with a `test:` key, it will be treated as a "test block".
If the YAML block does not contain a `test:` key, it will be ignored.

Any "test block" will require at least the `test:` key and "method" key that contains the URL - the "method" must be `get`, `post`, `put`, `patch`, `delete`, `head`, or `options`. Other keys are optional.

1. Create a test file (`my-api.resty`):

```markdown
# My API Tests

You can document your tests using standard Markdown syntax.

```yaml
test: get-users
get: https://jsonplaceholder.typicode.com/users
``    <-- only using two backticks here to avoid ending the example markdown block

```yaml
test: create-post
post: https://jsonplaceholder.typicode.com/posts
body: |
  {
    "title": "Test Post",
    "body": "This is a test",
    "userId": 1
  }
capture:
  post_id: id
``    <-- only using two backticks here to avoid ending the example markdown block
```

2. Run the tests:

```powershell
resty my-api.resty
```

## File Format

Resty supports two file extensions:

- **`.resty`** - Primary extension (recommended)
- **`.rest`** - Alternative extension

Both use the same format: Markdown with embedded YAML code blocks.

## Test Syntax

### Basic Test Structure

```yaml
test: test-name              # Required: unique test name
get: https://api.example.com # HTTP method and URL
# Supported methods: get, post, put, patch, delete, head, options
```

### Headers and Authentication

```yaml
test: authenticated-request
post: https://api.example.com/data
headers:
  authorization: Bearer $token
  content-type: application/json
  X-Custom-Header: custom-value
  User-Agent: MyApp/1.0
```

### Request Body

```yaml
test: post-data
post: https://api.example.com/users
body: |
  {
    "name": "John Doe",
    "email": "john@example.com"
  }
```

### Capture and Expectations

```yaml
test: login
post: https://api.example.com/auth
body: |
  {
    "username": "admin",
    "password": "secret"
  }
expect:
  status: 200
  headers:
    Content-Type: application/json; charset=utf-8
    X-Request-Id: $request_id
capture:
  # Extract values from JSON response using JSONPath syntax
  token: $.auth.token
  user_id: $.user.id
  expires: $.auth.expires_at
```

```yaml
# Example: Expect a 404 but still capture error information
# The test passes when status is 404 and capture is best-effort.
test: get-missing-resource
get: https://api.example.com/missing
expect:
  status: 404
capture:
  error_message: $.error.message
```

### Variables

```yaml
# File-level variables
variables:
  base_url: https://api.example.com
  api_version: v1

test: use-variables
get: $base_url/$api_version/users
headers:
  authorization: Bearer $token
```

### Environment Variables

```yaml
test: env-vars
get: $env:API_BASE_URL/users
headers:
  authorization: Bearer $env:API_TOKEN
```

### Include Files

```yaml
# Load shared variables from external files
include:
  - variables.yaml
  - secrets.yaml
```

## Command Line Usage

### Note about Flags

The command line flag-operator is completely interchangeable: `-`, `--`, or `/`.
For example, there is no difference between `-o`, `--o`, and `/o`.
Same goes for `-help`, `--help`, and `/help`.

Therefore, there is no chaining of single-dash flags like `-rv`  to mean `-r -v`.

> I won't change that behavior, because I'm tired of not knowing what to use
  for flags in different environments, because every tool does it differently.

Technically, all `-` and `/` characters are removed at the beginning of each flag.
```csharp
while (arg.StartsWith('-') || arg.StartsWith('/')) {
  isFlag = true;
  arg = arg.Substring(1);
}
```
So you can even do `---verbose` or `////help` if you want.

### Basic Commands

```powershell
resty [--all|-a]                # Run all tests in current directory recursively
resty --help|-h|-?              # Show help
resty --version|-v              # Show detailed version (-v shows only version number)
resty --verbose|-e              # Enable verbose output
```

### Test Selection

```powershell
resty auth/                     # Run all tests in auth/ directory (recursive)
resty auth/ -r false            # Run tests only in auth/ directory (non-recursive)
resty auth/login.resty          # Run specific file
resty -t login                  # Run test named 'login'
resty -t login,logout           # Run multiple specific tests
resty -f auth                   # Run tests containing 'auth' in name
resty -f api,user               # Run tests matching any pattern
resty --list                    # List all available tests
resty --dry-run                 # Validate tests without running
```

### Output Formats

```powershell
resty -o text                   # Default console output
resty -o json                   # JSON output
resty -o xml                    # JUnit XML for CI
resty -o html                   # Interactive HTML report
resty -s results.json           # Save to file
resty -o html -s report.html    # HTML report saved to file
```

### Advanced Options

```powershell
resty --timeout 60              # Set request timeout in seconds
resty --recursive false         # Search only top-level directory
resty --parallel 4              # Parallel execution (future)
resty --color false             # Disable colored console output
resty -v -o json -s full.json   # Verbose JSON output to file
resty -o html -s report.html    # Interactive HTML report
```

## Output Examples

### Console Output

```
═══════════════════════════════════════════════════════════════════
                          TEST RESULTS
═══════════════════════════════════════════════════════════════════

Total Tests:    3
Passed:         2 (66.7%)
Failed:         1
Skipped:        0
Duration:       1.23s

📁 api-tests.resty
   /path/to/api-tests.resty

  ✅ get-users (0.456s)
  ✅ create-post (0.234s)
  ❌ delete-post (0.123s)
     Error: HTTP 404 Not Found

═══════════════════════════════════════════════════════════════════
❌ TESTS FAILED (2/3 passed in 1.23s)
═══════════════════════════════════════════════════════════════════
```

### JSON Output

```json
{
  "summary": {
    "totalTests": 3,
    "passedTests": 2,
    "failedTests": 1,
    "skippedTests": 0,
    "passRate": 66.67,
    "duration": 1.234,
    "startTime": "2025-01-16T10:30:00Z",
    "endTime": "2025-01-16T10:30:01Z"
  },
  "results": [...],
  "metadata": {
    "tool": "Resty",
    "version": "1.0.0",
    "environment": {
      "os": "Windows 11",
      "runtime": ".NET 9.0"
    }
  }
}
```

## Advanced Features

### JSONPath Reference

Resty uses JSONPath syntax for extracting values from JSON responses. Here's a comprehensive guide:

#### Basic Syntax

| JSONPath         | Description         | Example Response             | Extracted Value |
| ---------------- | ------------------- | ---------------------------- | --------------- |
| `$.field`        | Root level field    | `{"name": "John"}`           | `"John"`        |
| `$.nested.field` | Nested object       | `{"user": {"name": "John"}}` | `"John"`        |
| `$.array[0]`     | First array element | `{"items": [1, 2, 3]}`       | `1`             |
| `$.array[-1]`    | Last array element  | `{"items": [1, 2, 3]}`       | `3`             |
| `$.array[*]`     | All array elements  | `{"items": [1, 2, 3]}`       | `[1, 2, 3]`     |

#### Real-World Examples

```yaml
# API Response:
# {
#   "result": {
#     "token": "eyJhbGciOiJIUzI1NiIs...",
#     "user": {
#       "id": 123,
#       "email": "user@example.com"
#     }
#   },
#   "status": "success"
# }

test: login
post: /api/auth
capture:
  # Extract the token
  auth_token: $.result.token

  # Extract user information
  user_id: $.result.user.id
  user_email: $.result.user.email

  # Extract status
  api_status: $.status
```

```yaml
# API Response with Arrays:
# {
#   "data": {
#     "users": [
#       {"id": 1, "name": "Alice"},
#       {"id": 2, "name": "Bob"}
#     ]
#   }
# }

test: get-users
get: /api/users
capture:
  # Get first user's ID
  first_user_id: $.data.users[0].id

  # Get all user names
  all_names: $.data.users[*].name

  # Get last user's name
  last_user_name: $.data.users[-1].name
```

#### Common Patterns

- **Authentication**: `token: $.result.token` or `token: $.access_token`
- **User Data**: `user_id: $.user.id`, `email: $.user.profile.email`
- **API Results**: `data: $.result.data`, `status: $.status`
- **Error Messages**: `error: $.error.message`
- **Arrays**: `first_item: $.items[0]`, `all_items: $.items[*]`

#### Notes

- Always start JSONPath expressions with `$` (root)
- Use `.` for object property access
- Use `[index]` for array element access
- Use `[*]` to select all array elements
- Use `[-1]` for the last array element
- Extracted variables become available for subsequent tests

### Variable Precedence

1. **Captured variables** (from previous test responses) - Highest
2. **File-level variables** (in YAML blocks)
3. **Included variables** (from external files)
4. **Environment variables** (`$env:NAME`) - Lowest

### Response Extraction

Capture strictness:
- For 2xx responses (except 204 No Content), all capture paths are treated as required. If any capture fails (missing path, invalid JSON, empty body), the test fails with a "Capture failed" error.
- For non-2xx responses and 204 No Content, capture is best-effort and never causes the test to fail.

Header expectations:
- Names are case-insensitive; values are case-sensitive and compared exactly after trimming whitespace.
- Values support variable substitution (e.g., `$trace_id`).
- Header expectations are only validated if the status expectation passes (if present), or if there is no status expectation and the response is 2xx.

Use JSONPath syntax to extract values from JSON responses:

```yaml
capture:
  # Basic field extraction
  simple_field: $.name

  # Nested object properties
  nested_field: $.user.profile.email

  # Array elements
  first_item: $.items[0].id
  last_item: $.items[-1].id

  # Array properties
  all_names: $.items[*].name

  # Complex paths
  token: $.result.auth.token
  user_id: $.data.user.id
```

### Test Dependencies

Resty supports explicit test dependencies using the `requires` property.
When a test has dependencies, those dependencies are automatically run first:

```yaml
# Test 1: Login (no dependencies)
test: login
post: /auth/login
capture:
  token: $.access_token
```

```yaml
# Test 2: Requires login test to run first
test: protected-endpoint
requires: login
get: /api/protected
headers:
  authorization: Bearer $token
```

#### Single Dependency

```yaml
test: get-profile
requires: login
get: /api/profile
headers:
  authorization: Bearer $token
```

#### Multiple Dependencies

```yaml
test: update-profile
requires:
  - login
  - get-profile
put: /api/profile
headers:
  authorization: Bearer $token
body: |
  {
    "name": "Updated Name"
  }
```

#### Dependency Chain

Dependencies can form chains - Resty automatically resolves the correct execution order:

```yaml
test: login
post: /auth/login
capture:
  token: $.access_token
```

```yaml
test: get-profile
requires: login
get: /api/profile
headers:
  authorization: Bearer $token
capture:
  user_id: $.id
```

```yaml
test: update-profile
requires: get-profile  # This will run login → get-profile → update-profile
put: /api/profile
headers:
  authorization: Bearer $token
```

#### Running Specific Tests

When you run a specific test, its dependencies are automatically included:

```bash
# This will run 'login' first, then 'update-profile'
resty test.rest -t update-profile
```

#### Error Handling

- **Missing dependency**: Exit code 3 if a required test doesn't exist
- **Circular dependency**: Exit code 4 if tests form a dependency loop
- **Dependency failure**: If a dependency test fails, dependent tests are skipped

### Recursive Directory Search

By default, Resty searches directories recursively for test files:

```powershell
# These are equivalent (recursive is default)
resty auth/
resty auth/ --recursive true
resty auth/ -r true

# Search only the specified directory (no subdirectories)
resty auth/ --recursive false
resty auth/ -r false
```

Use `--recursive false` when:
- You have a large project with many subdirectories
- You want to run only tests in a specific directory level
- You need to avoid running tests in nested folders

## VSCode Integration

For the best development experience, add to your VSCode settings:

```json
{
  "files.associations": {
    "*.resty": "markdown",
    "*.rest": "markdown"
  }
}
```

This provides:

- Syntax highlighting for YAML blocks.
- Markdown formatting.
- Code folding and indentation.
- IntelliSense support.

## Architecture

```powershell
Resty/
├── Resty.Core/              # Core parsing and execution logic
│   ├── Helpers/             # Helper classes and utilities
│   ├── Models/              # Domain models
│   ├── Parsers/             # File and YAML parsing
│   ├── Variables/           # Variable resolution engine
│   ├── Execution/           # HTTP execution and test running
│   └── Output/              # Result formatters (Console, JSON, XML)
├── Resty.Cli/               # Command-line interface
└── Resty.Tests/             # Unit and integration tests
```

## Exit Codes

- **0** - All tests passed
- **1** - One or more tests failed
- **2** - Internal error or invalid configuration
- **3** - Missing test dependency
- **4** - Circular test dependency detected

## Development

- The .NET SDK is required (version 9.0 or later).
- An. .editorconfig is included for consistent formatting, use the VSCode extension (id: EditorConfig.EditorConfig).

```powershell
# Clone and build
git clone <repository>
cd Resty
dotnet restore
dotnet build

# Run tests
dotnet test

# Run CLI
dotnet run --project Resty.Cli -- --help

# Create package
dotnet pack
```
