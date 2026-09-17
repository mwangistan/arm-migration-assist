# Validation (Feature 4)

Builds and tests the changed repository and backs the results dashboard.

| Subfolder | User story |
|-----------|------------|
| `BuildValidation/` | 4.1 Validation Framework |
| `Dashboard/` | 4.2 Results Dashboard (backend support) |

Reference repository demos (Story 4.3) live in the top-level `samples/` folder.

## Architecture boundary

Validation consumes Feature 3's `migration-result.json` plus the acceptance
checks embedded in `MigrationPlanV1`. It owns execution on ARM64 hardware or an
ARM64 runner: build verification, functional and reliability checks,
performance comparison, packaging/install checks, accessibility, and Windows
experience validation.

Every result should retain `planId`, work-item ID, and assessment evidence IDs so
failures can be traced back through the approved change to the original
repository fact. The current folders define this boundary and demo surface;
execution adapters can evolve independently. See
[the end-to-end architecture](../../docs/ARCHITECTURE.md).
