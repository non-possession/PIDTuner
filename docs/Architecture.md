# PIDTuner Architecture

PIDTuner is organized as a layered .NET 8 Windows desktop application.

## Layers

- `PIDTuner.Domain`: PID domain models, analysis primitives, and domain rules. This layer has no UI, database, PLC, or file-system dependencies.
- `PIDTuner.Application`: use-case orchestration, DTOs, and ports such as PLC clients, repositories, clocks, CSV import/export, and analysis services.
- `PIDTuner.Infrastructure`: adapters for PLC communication, SQLite persistence, CSV, configuration, and logging.
- `PIDTuner.Desktop`: WPF shell, views, view models, and UI services. UI code calls application services and does not directly access PLC or database adapters.

## First Delivery Focus

The first development phase should implement offline CSV import, curve-ready sample loading, analysis-window selection, and basic PID response metrics. PLC communication and parameter write-back remain behind interfaces until the offline analysis path is stable.

## Safety Rules

- Parameter write-back must require explicit human confirmation.
- UI must never block on PLC communication.
- PLC and database access must stay behind application interfaces.
- Analysis must run on offline data without PLC connectivity.
- Public data formats should be changed only after explicit confirmation.
- CSV fields are project metadata: each PID tuning project may define its own field profile through configuration while retaining the default sample fields as the starter profile.
