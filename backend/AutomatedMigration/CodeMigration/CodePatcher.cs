using AutomatedMigration.Generators;
using AutomatedMigration.Models;

namespace AutomatedMigration.CodeMigration;

// Story 3.3 (stretch) — transforms one architecture-specific code pattern.
// Interpreted stacks (Python/JS) usually have no such pattern, so this returns
// null. A native (C/C++) work item is where this generator earns its keep.
public sealed class CodePatcher : IMigrationGenerator
{
    public GeneratedPatch? Generate(WorkItem workItem, MigrationContext context) => null;
}
