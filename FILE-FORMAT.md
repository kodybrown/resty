# Resty File Format

Resty uses `.resty` and `.rest` files to define API tests. These files combine the readability of Markdown with embedded YAML blocks that define HTTP requests and tests.

## File Extensions

- **Primary**: `.resty` files (recommended)
- **Alternative**: `.rest` files (also supported)

## Why .resty/.rest Files?

1. **Clear Identification**: Easy to distinguish test files from regular documentation
2. **Tool Association**: Can be associated with Resty in your IDE/editor
3. **File Organization**: Easier to search for and organize test files
4. **Professional**: Shows this is a dedicated testing format
5. **Flexibility**: Choose the extension that fits your preference or team standards

> In the future, I may add support for specifying custom extensions in the future, but for now `.resty` and `.rest` are the only supported ones.

## VSCode Integration

To get proper syntax highlighting for `.resty` and `.rest` files in VSCode, add this to your workspace settings:

```json
{
  "files.associations": {
    "*.resty": "markdown",
    "*.rest": "markdown"
  }
}
```

#### JSONPath Functions (postfix, zero-argument, chainable)
Resty supports a set of simple functions you can append to the end of JSONPath expressions. These are evaluated after the base path is resolved.

Functions:
- length(), count(), size(): length for arrays and strings (null → 0)
- empty(): boolean for null/empty array/empty string/empty object
- type(): returns: array | object | string | number | boolean | null | date
- sum(), avg(), min(), max(): aggregates on numeric arrays (non-numerics ignored; empty → 0)
- distinct(): removes duplicates from arrays (order preserved)
- keys(): object → array of property names
- values(): object → array of property values
- to_number(), to_string(), to_boolean(): conversions (arrays mapped element-wise)
- trim(), lower(), upper(): string utilities (arrays mapped element-wise)

Examples:

```yaml
# Count items returned
expect:
  status: 200
  values:
    - key: $.items.length()
      op: greater_than
      value: 0
```

```yaml
# Ensure properties exist via keys().length()
expect:
  status: 200
  values:
    - key: $.data.keys().length()
      op: equals
      value: 5
```

```yaml
# Ensure uniqueness
expect:
  status: 200
  values:
    - key: $.ids.distinct().length()
      op: equals
      value: 10
```

```yaml
# Aggregates on arrays
expect:
  status: 200
  values:
    - key: $.nums.sum()
      op: equals
      value: 42
    - key: $.nums.avg()
      op: less_than
      value: 10
```

```yaml
# Convert and then aggregate
expect:
  status: 200
  values:
    - key: $.scores.to_number().max()
      op: greater_than_or_equal
      value: 90
```

Notes:
- Functions are zero-argument postfix and can be chained.
- Aggregates ignore non-numeric entries and yield 0 for empty arrays.
- Objects do not have length; use keys().length() to count properties.

This gives you full markdown syntax highlighting including:
- ✅ Syntax highlighting for YAML blocks
- ✅ Markdown formatting (headers, lists, etc.)
- ✅ Code completion in YAML sections
- ✅ Proper folding and indentation

## File Structure

A `.resty` file is structured markdown with embedded YAML blocks:

```markdown
# My API Tests

Description of your test suite in markdown.

## Test Group 1

```yaml
test: test-name
get: https://api.example.com/endpoint
``    <-- only using two backticks here to avoid ending the example markdown block

More markdown documentation...

```yaml
test: another-test
post: https://api.example.com/data
body: |
  {
    "key": "value"
  }
capture:
  # Extract response values using JSONPath (note the $ prefix)
  result_id: $.result.id
  status: $.status
``    <-- only using two backticks here to avoid ending the example markdown block
```

## File Extension Choice

Both `.resty` and `.rest` extensions work identically. Choose based on:

- **`.resty`** - Clearly identifies the tool and format
- **`.rest`** - Shorter, follows REST API naming conventions

The tool will automatically discover both file types in your directories.

## Expectations

Use the `expect:` section to assert response properties. Currently supported:
- `status`: exact HTTP status code that must be returned. If omitted, Resty uses standard success semantics (2xx).
- `headers`: a dictionary of expected response headers (names are case-insensitive, values are case-sensitive). Values may contain variables.
- `values`: a list of JSON value assertions evaluated against the response body.

Each `values` rule has:
- `key`: JSONPath expression
- `op`: operation (equals, not_equals, contains, not_contains, startswith, not_startswith, endswith, not_endswith, greater_than, greater_than_or_equal, less_than, less_than_or_equal, exists, not_exists). Aliases: eq, ne, gt, gte, lt, lte, starts_with, ends_with.
- `value`: expected value (not required for exists/not_exists). Supports `$null`, `$empty`, variables.
- `store_as`: optional variable name to capture the extracted value.
- `ignore_case`: optional boolean, default true for string ops.

Use the `expect:` section to assert response properties. Currently supported:
- `status`: exact HTTP status code that must be returned. If omitted, Resty uses standard success semantics (2xx).

Examples:

```yaml
# Pass on 200 and assert values
expect:
  status: 200
  headers:
    Content-Type: application/json; charset=utf-8
    X-Trace-Id: $trace_id
  values:
    - key: $.response.id
      op: equals
      value: 12345
    - key: $.response.success
      op: equals
      value: true
    - key: $.response.name
      op: contains
      value: test
```

```yaml
# Intentionally expect a 404 and still consider the test passed
expect:
  status: 404
  headers:
    Content-Type: application/json
```

## HTTP Mocking

Fields in YAML:
- Per-test:
  - mock_only: boolean
  - mock: Inline mock response with fields: status, headers, body, content_type, delay_ms, sequence[]
- File-level:
  - mocks: List of { method, url, status?, headers?, body?, content_type?, delay_ms?, sequence[]? }
  - mocks_files: List of JSON files, each containing an array of the same object structure as in mocks

Notes:
- Inline mock applies only to the test it is defined in and takes precedence.
- File-level mocks apply to any test in the same file (including required tests executed under this file’s context), but are not imported from included files. To share mocks across files, use mocks_files in each file.
- Variables in url and body are resolved at request time.
- sequence returns the first/second/... entry per successive matching request within the same test (sticky-last when exhausted). Combine with retry to simulate transient errors.
- delay_ms can be specified at the top-level or per sequence entry (per-entry overrides).
- Duplicates across mocks_files: last one wins; Resty prints a warning.

Examples:

```yaml path=null start=null
# Inline mock only test (no get/post)
test: mock-inline
mock_only: true
mock:
  status: 200
  body: { ok: true }
```

```yaml path=null start=null
# File-level mocks + external files
mocks:
  - method: GET
    url: $base/users
    status: 200
    body: { users: [{ id: 1 }] }
mocks_files:
  - mocks/users.json
```

```json path=null start=null
[
  { "method": "GET", "url": "$base/users", "status": 200, "body": { "users": [ { "id": 1 } ] } }
]
```

## Best Practices

### File Organization

- Use descriptive filenames: `auth-tests.resty`, `user-api.rest`, `payments.resty`
- Group related tests in the same file
- Use markdown headers to organize test sections
- Document your tests with regular markdown between YAML blocks
- Keep files focused on a specific API or feature area
- Choose either `.resty` or `.rest` consistently within your project
- Organize tests in directory structures - Resty searches recursively by default
- Use `--recursive false` if you need to limit search to specific directory levels

### Response Extraction

- Always use JSONPath syntax starting with `$` for response extraction
- **Correct**: `token: $.result.token`
- **Incorrect**: `token: result.token` or `token: auth.response.result.token`
- Use descriptive variable names: `auth_token`, `user_id`, `api_status`
- Extract only the values you need for subsequent tests
- Document complex JSONPath expressions with comments

### Request Body

You can supply the body as either a raw string (JSON or otherwise) or as a structured YAML object that will be serialized for you.

- If Content-Type is application/json (or omitted), a structured body will be serialized to JSON.
- If Content-Type is application/x-www-form-urlencoded and the body is a mapping, it will be URL-encoded as key=value&key2=value2.
- Structured bodies with any other Content-Type are not allowed and will cause an error.

Examples:

```yaml
# Raw string JSON
body: |
  {
    "name": "John Doe",
    "email": "john@example.com"
  }
```

```yaml
# Structured YAML → JSON
body:
  name: John Doe
  email: john@example.com
```

```yaml
# application/x-www-form-urlencoded from mapping
headers:
  content-type: application/x-www-form-urlencoded
body:
  username: $username
  password: $password
```

```yaml
headers:
  content-type: application/x-www-form-urlencoded
body: 'username=$username&password=$password'
```

### JSONPath Examples

```yaml
capture:
  # Basic field extraction
  token: $.access_token

  # Nested objects
  user_id: $.user.profile.id

  # Array elements
  first_item: $.items[0]
  last_item: $.items[-1]
  all_items: $.items[*]
```
