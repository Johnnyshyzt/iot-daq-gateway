# Northbound contract `northbound/1.0`

JSON Schema files in this directory are the stable payload contract for MQTT (when the sink is set to `v1`), HTTP push batches, and the read-only query API. Host serves them without login:

- `GET /api/contract/v1` lists `*.schema.json`
- `GET /api/contract/v1/{name}.schema.json` returns one schema
- `GET /api/query/v1/openapi.json` returns the query API document

Examples live in `examples/`. Point and device-status files named `*.legacy.json` match the default MQTT shape, which omits `schema` and `kind`. The integration guide is [../integration.md](../integration.md).
