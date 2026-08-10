# Deterministic API Workflows

## Purpose

This document is the durable source of truth for extending Resty from ordered
HTTP tests into safe, dependency-driven API workflows. The motivating use case
is sensor and uplink integration testing that must calculate UTC windows, send
typed payloads, conditionally create resources, and poll eventually consistent
endpoints.

The initiative must remain bounded and deterministic. Resty will not execute
arbitrary C#, JavaScript, shell commands, or external processes as part of the
file format.

## Status

| Slice | Scope | Status |
|---|---|---|
| 1 | Typed expression variables and type-preserving substitution | Complete and verified |
| 2 | Conditional execution, status lists, response metadata, and dependency-result gating | Complete and verified |
| 3 | `wait_before` and expectation-aware retry policies | Complete and verified |
| 4 | Spark sensor/uplink workflow examples and orchestration guidance | Complete and verified |

Slices 1 through 4 are implemented and verified in the current branch. Slice 4
was explicitly approved on 2026-08-12.

## Settled Expression Syntax

Expressions use `${{ expression }}`. Variable references inside an expression
may be bare identifiers or `$`-prefixed identifiers:

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
  reading_id: ${{ uuid() }}
```

Slice 1 supports:

- Arithmetic: `+`, `-`, `*`, `/`, `%`, and parentheses.
- Comparisons: `==`, `!=`, `<`, `<=`, `>`, and `>=`.
- Boolean operations: `&&`, `||`, `!`, plus `and`, `or`, and `not` aliases.
- UTC functions: `utc_now()` and `start_of_hour(value)`.
- Durations: `milliseconds()`, `seconds()`, `minutes()`, `hours()`, and
  `days()`.
- Epoch conversion: `unix_seconds()` and `unix_milliseconds()`.
- Formatting: `format(value, format)` using invariant .NET format strings.
- Generators: `uuid()`, `random_int()`, `random_int(max)`,
  `random_int(min, max)`, and `random_hex(length)`.

`random_int` upper bounds are exclusive. `random_hex` returns uppercase text of
the requested length and accepts lengths from 1 through 1024.

## Typed Variable Semantics

- Computed variables are resolved lazily, so declaration order does not matter.
- A computed value is cached for the life of the variable store. Reusing a UUID,
  random value, or `utc_now()`-derived value is stable throughout one suite run.
- `utc_now()` uses one UTC clock snapshot for the suite's variable store.
- Missing variable references fail with a clear error.
- Cyclic references fail with the complete dependency path.
- Date/time arithmetic supports date/time plus or minus a duration, date/time
  minus date/time, and duration plus or minus duration.
- Numeric arithmetic preserves integral results where possible and otherwise
  uses decimal values. Division returns a decimal value.

## Substitution Rules

- A structured YAML scalar that is exactly `$variable` or
  `${{ expression }}` preserves the underlying numeric, boolean, date/time,
  duration, GUID, or string value before serialization.
- A variable or expression embedded in a larger string is formatted invariantly.
- Date/time interpolation defaults to round-trip `O` formatting in UTC.
- Duration interpolation uses the invariant constant (`c`) format.
- Boolean interpolation uses lowercase `true` and `false`.
- URLs and header values remain strings.
- Raw string bodies remain strings.
- Structured JSON bodies preserve numbers and booleans rather than quoting them.
- Existing `$variable`, `$env:NAME`, and `$RandomNumber` syntax remains valid.
  `$RandomNumber` retains its legacy five-digit string behavior.
- Unquoted YAML numeric and boolean scalars are interpreted as typed values.
  Quote identifiers and other values that resemble YAML scalars but must remain
  strings.

## Conditional Workflow Contract (Slice 2)

The implemented conditional workflow syntax is:

```yaml
test: sensor_lookup
get: $host/demo/devices/$sensor_ref
expect:
  status: [200, 404]
capture:
  sensor_lookup_status: $response.status

test: sensor_create
requires: sensor_lookup
when: ${{ sensor_lookup_status == 404 }}
post: $host/demo/devices
body:
  label: DEMO-$sensor_ref
  group: $sensor_group
  reference: $sensor_ref
expect:
  status: 201
```

Slice 2 implements these requirements:

- `expect.status` accepts either one integer or a list of integers.
- Response metadata capture begins with `$response.status`.
- `when` uses the same bounded expression evaluator as variables.
- A test skipped because `when` is false satisfies downstream dependencies.
- A test skipped because a required test failed does not satisfy downstream
  dependencies.
- A failed required test skips its dependent tests.
- Existing topological ordering, missing-dependency validation, and cycle
  detection remain unchanged.

The result model must distinguish a conditional skip from a dependency-failure
skip. Human and machine-readable outputs must retain the reason.

## Timing And Retry Contract (Slice 3)

The implemented timing and retry syntax is:

```yaml
wait_before: ${{ seconds(2) }}

retry:
  attempts: 10
  delay: ${{ milliseconds(500) }}
  backoff: fixed
  statuses: [404, 409, 500, 502, 503, 504]
  expectation_failures: true
```

Slice 3 implements these requirements:

- Scalar `retry: N` remains backward compatible.
- `wait_before` runs once before the first request attempt.
- Retry delays honor cancellation.
- Fixed backoff is implemented first, with a model that can add exponential
  backoff later.
- Configured HTTP statuses can trigger retries.
- Failed header and JSON value expectations can trigger retries when
  `expectation_failures` is enabled.
- Captured variables are committed only after the successful final attempt.

A standalone `wait:` step is deferred. It requires a broader executable-step
model and is not necessary for the first sensor/uplink workflows.

## Compatibility Requirements

- Existing `.resty` and `.rest` files must parse and execute unchanged.
- Variable precedence remains captured, file, included, then environment.
- YAML-native numeric and boolean values remain typed.
- Raw request bodies and form bodies retain their current behavior except that
  exact typed substitutions now format invariantly when form-encoded.
- Existing scalar retry behavior, dependency ordering, mock behavior, capture
  strictness, and output formats remain compatible through Slice 3.
- No new general-purpose scripting or process-execution surface is permitted.

## Pre-Slice 2 Dependency Characterization

Inspection on 2026-08-10 confirmed that `DependencyResolver` already
topologically ordered `requires`, detected missing tests, and detected cycles.
Before Slice 2, `TestSuiteRunner` did not inspect prior `TestResult` values
before running a dependent test.

The Slice 1 characterization test proved that `required_failure` failed while
`dependent` still executed and passed. Slice 2 replaced that characterization
with desired gating tests. Failed, disabled, and dependency-skipped required
tests now block dependents; dependency-failure skips propagate transitively.
Conditional skips satisfy dependents.

## Slice 1 Verification Matrix

| Behavior | Coverage |
|---|---|
| Previous completed UTC hour | Focused expression unit test |
| Date/time plus or minus duration | Focused expression unit test |
| Unix seconds and milliseconds | Focused expression unit test |
| Numeric multiplication and addition | Focused expression unit test |
| Operator precedence and parentheses | Focused expression unit test |
| Declaration-order-independent references | Focused expression unit test |
| Missing references and cycles | Focused failure-path unit tests |
| Typed numeric and boolean JSON fields | Executor request-body test |
| Date/time interpolation in URLs | Executor request-info test |
| Unquoted YAML scalar typing | Parser and included-variable tests |
| `$variable`, `$env:NAME`, and `$RandomNumber` | Existing and focused compatibility tests |
| Example `.resty` syntax | `examples/typed-expressions.resty` |
| Existing dependency-result gap | Runner characterization test |

Verification completed on 2026-08-10:

- `make verify`: build succeeded with zero warnings and errors; 100 tests
  passed; formatting and required-document checks passed.
- `make run ARGS="examples/typed-expressions.resty"`: both self-contained
  example tests passed.
- `git diff --check`: no whitespace errors.

## Slice 2 Verification Matrix

| Behavior | Coverage |
|---|---|
| Scalar `expect.status` compatibility | Parser and existing executor tests |
| Status lists accept any declared code | Parser, executor, and workflow tests |
| Invalid status-list entries fail clearly | Parser failure-path test |
| `$response.status` captures an integer without JSON | Executor and workflow tests |
| Boolean `when` true executes | Workflow runner test |
| Boolean `when` false conditionally skips | Workflow runner test |
| Non-boolean and missing-variable conditions fail before request | Runner failure-path tests |
| Conditional skip satisfies downstream dependency | Integrated workflow test |
| Required failure skips direct and transitive dependents | Dependency gating test |
| Disabled required test blocks dependent | Dependency gating test |
| Human and machine outputs retain skip reason | Formatter coverage across all output modes |
| Self-contained conditional workflow syntax | `examples/conditional-workflow.resty` |

Verification completed on 2026-08-10:

- Focused Slice 2 test selection: 12 tests passed.
- `make verify`: build succeeded with zero warnings and errors; 111 tests
  passed; formatting and required-document checks passed.
- `make run ARGS="examples/conditional-workflow.resty"`: all three
  self-contained workflow tests passed.
- `make run ARGS="examples/typed-expressions.resty"`: both Slice 1 regression
  examples passed.
- `git diff --check`: no whitespace errors.

## Slice 3 Verification Matrix

| Behavior | Coverage |
|---|---|
| Scalar `retry: N` retains retry-count semantics | Parser and existing mock sequence tests |
| Structured attempts use total-attempt semantics | Parser and executor tests |
| Fixed retry delay resolves bounded duration expressions | Executor delay-recording tests |
| `wait_before` runs once before all attempts | Executor delay-order test |
| Configured statuses trigger retries | Executor sequence test |
| Unconfigured statuses stop immediately | Executor sequence test |
| JSON expectation failures can retry | Executor sequence test |
| Header expectation failures can retry | Executor sequence test |
| Expectation retry remains opt-in | Executor failure-path test |
| Retry delays honor cancellation | Executor cancellation test |
| Invalid timing values fail before request | Executor failure-path test |
| Captures commit only from the successful final attempt | Runner integration test |
| Self-contained timing/retry workflow syntax | `examples/retry-workflow.resty` |

Verification completed on 2026-08-10:

- Focused Slice 3 test selection: 19 tests passed.
- `make verify`: build succeeded with zero warnings and errors; 124 tests
  passed; formatting and required-document checks passed.
- `make run ARGS="examples/retry-workflow.resty"`: both self-contained retry
  workflow tests passed.
- Conditional and typed-expression regression examples also passed.
- `git diff --check`: no whitespace errors.

## Slice 4 Spark Workflow Example

The public Spark examples use a fictional sensor API and synthetic data to
demonstrate this orchestration order.

1. Look up a generated sensor reference and accept `200` or `404`.
2. Create the sensor only after `404` and wait for it to become readable.
3. Post two readings in the previous completed UTC hour.
4. Poll the readings and check their delta before requesting an hourly summary.

The live template requires an explicit demo host and an ignored private
configuration file. The hourly-summary check is disabled by default. See the
[Spark example README](../../examples/spark/README.md) for live setup and
handling of request and response reports.

## Slice 4 Verification Matrix

| Behavior | Coverage |
|---|---|
| Lookup accepts an existing or missing sensor | Live template and mock suite |
| Missing sensor is conditionally created | Live template and mock suite |
| Readiness retry gates both uploads | Live template and mock suite |
| UTC window and synthetic readings retain their types | Live template and mock suite |
| Raw reads retry on eventual expectations | Live template and mock suite |
| Reading delta verifies the expected value | Live template and mock suite |
| Hourly summary is optional and can be retried | Live template default and mock retry |
| Live settings are supplied locally | Ignored demo configuration and explicit host |

Run the mock and validate the template from the repository root.

```powershell
make run ARGS="--path examples/spark/sensor-uplink.mock.resty"
make run ARGS="--path examples/spark/sensor-uplink.resty --dry-run"
```

Running the template against a disposable API creates a sensor and writes
synthetic measurements.
