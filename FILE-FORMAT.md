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

## Variables And Typed Expressions

Resty supports file variables, included YAML variables, captured variables,
environment variables, and the legacy `$RandomNumber` value. The precedence
order is captured, file, included, then environment.

Use `${{ expression }}` for safe calculated values:

```yaml
variables:
  hour_end: ${{ start_of_hour(utc_now()) }}
  hour_start: ${{ hour_end - hours(1) }}
  read_time: ${{ hour_start + minutes(20) }}
  epoch_seconds: ${{ unix_seconds(read_time) }}
  epoch_milliseconds: ${{ epoch_seconds * 1000 }}
  initial_value: 1000.0
  next_value: ${{ initial_value + 1.25 }}
  sensor_ref: ${{ random_hex(16) }}
  deduplication_id: ${{ uuid() }}
```

### Operators

- Arithmetic: `+`, `-`, `*`, `/`, `%`.
- Comparisons: `==`, `!=`, `<`, `<=`, `>`, `>=`.
- Boolean: `&&`, `||`, `!`, plus `and`, `or`, `not`.
- Parentheses control precedence.

Numeric operators accept YAML numeric values and numeric expression results.
Date/time values support addition and subtraction with durations. Subtracting
two date/time values produces a duration.

### Functions

- `utc_now()` returns the suite's UTC clock snapshot.
- `start_of_hour(value)` truncates a date/time value to its UTC hour.
- `milliseconds(value)`, `seconds(value)`, `minutes(value)`, `hours(value)`,
  and `days(value)` create durations.
- `unix_seconds(value)` and `unix_milliseconds(value)` convert date/time values
  to Unix epoch integers.
- `format(value, format)` uses invariant .NET format strings.
- `uuid()` returns a GUID value.
- `random_int()` returns a non-negative integer.
- `random_int(max)` returns an integer from zero through `max - 1`.
- `random_int(min, max)` returns an integer from `min` through `max - 1`.
- `random_hex(length)` returns uppercase hexadecimal text. Length must be from
  1 through 1024.

Variables inside expressions may be bare (`hour_start`) or `$`-prefixed
(`$hour_start`). Computed variables resolve regardless of YAML declaration
order. Missing references and cycles are errors. Computed values are cached for
the suite run, so repeated UUID, random, and time-derived references stay
consistent.

### Typed Substitution

When a structured YAML scalar is exactly `$variable` or
`${{ expression }}`, Resty preserves the underlying type. When a substitution
is embedded in a larger string, Resty formats it invariantly.

```yaml
body:
  epochSeconds: $epoch_seconds
  epochMilliseconds: ${{ epoch_seconds * 1000 }}
  enabled: $enabled
  label: DEMO-$sensor_ref
```

Numbers and booleans remain JSON numbers and booleans. Date/time values use
round-trip `O` formatting when converted to text; durations use the constant
`c` format. URLs and headers always remain strings. Raw string bodies also
remain strings.

Existing `$variable`, `$env:NAME`, and `$RandomNumber` substitutions remain
supported. `$RandomNumber` retains its legacy five-digit string behavior.
Unquoted YAML numbers and booleans are typed; quote identifiers or other values
that resemble YAML scalars when they must remain strings.

## Expectations

Use the `expect:` section to assert response properties. Currently supported:
- `status`: one HTTP status integer or a list of acceptable integers. If omitted, Resty uses standard success semantics (2xx).
- `headers`: a dictionary of expected response headers (names are case-insensitive, values are case-sensitive). Values may contain variables.
- `values`: a list of JSON value assertions evaluated against the response body.

Each `values` rule has:
- `key`: JSONPath expression
- `op`: operation (equals, not_equals, contains, not_contains, startswith, not_startswith, endswith, not_endswith, greater_than, greater_than_or_equal, less_than, less_than_or_equal, exists, not_exists). Aliases: eq, ne, gt, gte, lt, lte, starts_with, ends_with.
- `value`: expected value (not required for exists/not_exists). Supports `$null`, `$empty`, variables.
- `store_as`: optional variable name to capture the extracted value.
- `ignore_case`: optional boolean, default true for string ops.

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

```yaml
# Accept either an existing resource or a not-found response
expect:
  status: [200, 404]
capture:
  lookup_status: $response.status
```

`$response.status` captures the actual status as an integer and does not
require a JSON response body. Other `capture` values continue to use JSONPath.

## Conditional Execution And Dependency Results

Use `requires` to declare one or more prerequisite tests. Resty validates
missing dependencies and cycles, then executes tests in topological order.

Use `when` with a bounded expression when a step is optional:

```yaml
test: sensor_lookup
get: $host/demo/devices/$sensor_ref
expect:
  status: [200, 404]
capture:
  sensor_lookup_status: $response.status
```

```yaml
test: sensor_create
requires: sensor_lookup
when: ${{ sensor_lookup_status == 404 }}
post: $host/demo/devices
body:
  label: DEMO-$sensor_ref
expect:
  status: 201
```

Rules:

- `when` must resolve to a boolean. A non-boolean value or expression error
  fails the test before any request is sent.
- A false condition produces a conditional skip. That skip satisfies
  downstream dependencies because the optional step completed as designed.
- A failed required test produces a dependency-failure skip in each dependent.
  Dependency-failure skips propagate transitively and do not satisfy later
  dependencies.
- A disabled required test does not satisfy downstream dependencies.
- Skip classifications and explanations are retained in human-readable, JSON,
  agent, HTML, and JUnit XML output.

## Timing And Retry Policies

`wait_before` delays once before the first request attempt. Its value must
resolve to a non-negative duration:

```yaml
wait_before: ${{ seconds(2) }}
```

The historical scalar retry syntax remains supported:

```yaml
retry: 2
```

Scalar `retry: N` means one initial attempt plus `N` retries. It preserves the
existing retryable statuses (`408`, `429`, `500`, `502`, `503`, and `504`),
retryable network exceptions, one-second initial delay, exponential delay
progression, and 30-second delay cap.

Use a structured policy for deterministic workflow retries:

```yaml
retry:
  attempts: 10
  delay: ${{ milliseconds(500) }}
  backoff: fixed
  statuses: [404, 409, 500, 502, 503, 504]
  expectation_failures: true
```

Structured fields:

- `attempts`: total attempts including the first request; must be at least 1.
- `delay`: non-negative duration between attempts; defaults to no delay.
- `backoff`: currently only `fixed` is supported.
- `statuses`: response statuses that can trigger another attempt. When omitted,
  Resty uses the historical retryable status set.
- `expectation_failures`: when true, failed header and JSON value expectations
  can trigger another attempt; defaults to false.

Configured status retries apply only when the response does not already satisfy
`expect.status`. Retry and wait delays honor cancellation. Captures and
`store_as` variables are returned to the suite only after a successful final
attempt, so failed attempts cannot leak transient state.

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

- Use `$response.status` to capture the integer HTTP response status.
- Use JSONPath syntax starting with `$` for response-body extraction.
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
- Exact `$variable` and `${{ expression }}` values retain their type before
  JSON serialization. Form values are converted using invariant formatting.

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

### Multipart Form Uploads

Use `multipart` as an ordered list of parts. Each part has a `name` and either
a `value` for a UTF-8 text field or a `file` path for a binary upload. Text
values support captured variables and expressions, including an empty string
written as `value: ""`. Use multiple entries with the same name when the API
expects repeated fields or files.

Relative paths resolve from the `.resty` or `.rest` file declaring the upload,
including when that file is included by another suite. Resty sends the file's
basename and original bytes. File parts accept an optional `content_type`,
which defaults to `application/octet-stream`. A missing fixture fails the test
before sending the request and reports the test name and resolved path.

Resty generates the multipart boundary and request `Content-Type` together.
Use `multipart` without `body` or a request-level `Content-Type` header.

#### Upload a document with a text field

The `create_record` test supplies `$record_id`. The upload captures the new
attachment ID for follow-up tests. In these examples, `$host` is the API base
URL and `$access_token` is the bearer token supplied by the suite.

```yaml
test: upload_attachment
requires: create_record
post: $host/api/attachments
headers:
  Authorization: Bearer $access_token
multipart:
  - name: RecordId
    value: $record_id
  - name: Attachment
    file: fixtures/sample.pdf
    content_type: application/pdf
expect:
  status: 200
capture:
  attachment_id: $.result.id
```

```yaml
test: download_attachment
requires: upload_attachment
get: $host/api/attachments/$attachment_id/download
headers:
  Authorization: Bearer $access_token
expect:
  status: 200
```

#### Upload and retrieve a profile photo

The `create_test_member` test supplies `$member_id` for both requests.

```yaml
test: upload_member_photo
requires: create_test_member
post: $host/api/members/$member_id/photo
headers:
  Authorization: Bearer $access_token
multipart:
  - name: file
    file: fixtures/profile.png
    content_type: image/png
expect:
  status: 200
```

```yaml
test: get_member_photo
requires: upload_member_photo
get: $host/api/members/$member_id/photo
headers:
  Authorization: Bearer $access_token
expect:
  status: 200
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
