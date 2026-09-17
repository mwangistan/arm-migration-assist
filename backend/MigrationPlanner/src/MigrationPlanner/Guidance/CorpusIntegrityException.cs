namespace MigrationPlanner.Guidance;

public sealed class CorpusIntegrityException : Exception
{
    public CorpusIntegrityException(string message) : base(message) { }

    public CorpusIntegrityException(string message, Exception inner) : base(message, inner) { }
}
