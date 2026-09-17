# Feature 1: Repository Analysis

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
