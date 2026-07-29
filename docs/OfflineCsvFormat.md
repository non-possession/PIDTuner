# Offline CSV Format

The phase 1 offline-analysis import uses a stable CSV header:

```text
timestamp,sp,pv,mv,kp,ki_or_ti,kd_or_td,is_plc_connected,test_session_id,parameter_set_id
```

## Fields

- `timestamp`: ISO 8601 timestamp with offset.
- `sp`: set point.
- `pv`: process value.
- `mv`: manipulated value or control output.
- `kp`: proportional parameter.
- `ki_or_ti`: integral parameter representation, pending final Ki/Ti decision.
- `kd_or_td`: derivative parameter representation, pending final Kd/Td decision.
- `is_plc_connected`: `true` or `false`.
- `test_session_id`: GUID.
- `parameter_set_id`: optional GUID.

See `samples/offline-step-response.csv` for a minimal importable file.

## Project-Level Field Profiles

The sample header above is the default profile, not a permanent global limit.

Each PID tuning project should be able to keep its own CSV field profile as project metadata. Users should be able to manually add, rename, remove, and remap fields for a single project through configuration. The profile must define each field's stable key, display name, data type, unit, required flag, and semantic role.

The initial example profile lives at `config/pid-sample-fields.example.json`. Future implementation should load a copied project-specific profile rather than hard-coding the CSV fields in UI logic.
