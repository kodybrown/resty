# Spark Sensor And Uplink Workflows

These examples use a fictional sensor API to demonstrate a Resty workflow for
provisioning a sensor, posting two synthetic readings, and checking the results.

- `sensor-uplink.mock.resty` runs entirely in Resty's mock mode.
- `sensor-uplink.resty` is a live template for a disposable test API that
  implements the example routes and payloads.

## Verify The Workflow Locally

Run the mock suite from the repository root.

```powershell
make run ARGS="--path examples/spark/sensor-uplink.mock.resty"
```

Validate the live template without sending any requests.

```powershell
make run ARGS="--path examples/spark/sensor-uplink.resty --dry-run"
```

## Configure A Live Run

Create `sensor-uplink-demo.private.yaml` beside the template. The existing
`**/*.private.*` rule keeps it out of Git. Supply a disposable test credential
and sensor group using the following keys.

```yaml
variables:
  demo_authorization: "Bearer REPLACE_WITH_TEST_TOKEN"
  demo_sensor_group: demo-group
```

Set `RESTY_SPARK_DEMO_HOST` only for the invocation. The template requires
both this environment variable and the private include before it can send
requests. Replace the reserved example host with your disposable API's HTTPS
address.

```powershell
$previousDemoHost = $env:RESTY_SPARK_DEMO_HOST
try {
  $env:RESTY_SPARK_DEMO_HOST = 'https://spark.example.com'
  make run ARGS="--path examples/spark/sensor-uplink.resty"
} finally {
  $env:RESTY_SPARK_DEMO_HOST = $previousDemoHost
}
```

Live runs create a synthetic sensor and write two readings. Use an environment
where those changes are acceptable and clean them up afterward as needed.
Keep live run output and reports private. They can contain resolved
authorization headers, variable values, and response data.

## Workflow Order

1. Generate a sensor reference and look it up. Create it only after a `404`.
2. Wait for the sensor to become readable before posting two readings in the
   previous completed UTC hour.
3. Verify the raw readings, then check their expected delta.
4. Enable the optional hourly-summary check only when your test API provides
   that endpoint.
