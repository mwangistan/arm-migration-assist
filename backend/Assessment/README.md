# Assessment (Feature 1)

Turns a repository into a reproducible, evidence-backed inventory for Windows on
Arm planning. The `RepositoryDiscovery` executable orchestrates all four stories
and produces the `RepositoryAssessmentV1` contract consumed by Feature 2. It
exposes direct .NET, synchronous HTTP, queued HTTP, and Server-Sent Events (SSE)
integration paths while retaining one React and Fluent UI dashboard.

| Subfolder | User story |
|-----------|------------|
| `RepositoryDiscovery/` | 1.1 Repository Intake / 1.2 Technology Discovery |
| `DependencyScanner/` | 1.3 Dependency Scanner |
| `CodeCompatibility/` | 1.4 Architecture Compatibility Scanner |

Feature 1 reports repository facts, compatibility signals, scan coverage, and
explicit unknowns. Readiness scores and migration recommendations belong to
Feature 2 and are not inferred here.

## Architecture

`RepositoryDiscoveryService` opens an immutable repository snapshot, builds one
bounded file catalog, and runs technology, dependency, and code scanners over
that catalog in parallel. It combines their outputs with build and Windows
signals, validates cross-record evidence, and publishes
`RepositoryAssessmentV1`.

Anonymous API intake uses commit-pinned shared clones with Windows long-path
support and validates a cached clone's HEAD before reuse. Dependency scanning
reports architecture-neutral managed NuGet references as `ready`/`any-cpu`;
explicit architecture tokens and native packages retain stricter evidence-based
classification.

The API exposes direct, synchronous, queued, and SSE integration surfaces. The
product frontend uses queued jobs and live events; the planner consumes only the
completed contract. See [the system architecture](../../docs/ARCHITECTURE.md)
and [repository assessment details](RepositoryDiscovery/README.md).
