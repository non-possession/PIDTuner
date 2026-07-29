# Infrastructure Adapters

This project will host PLC, SQLite, CSV, logging, and configuration adapters.

Concrete adapters should implement interfaces from `PIDTuner.Application` and remain replaceable so the desktop UI and analysis code can run without a live PLC.
