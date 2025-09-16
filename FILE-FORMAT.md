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
success:
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

### JSONPath Examples

```yaml
success:
  # Basic field extraction
  token: $.access_token

  # Nested objects
  user_id: $.user.profile.id

  # Array elements
  first_item: $.items[0]
  last_item: $.items[-1]
  all_items: $.items[*]
```
