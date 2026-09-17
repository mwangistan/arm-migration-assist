namespace AutomatedMigration.CodeMigration;

// Minimal chat abstraction so the code transformer isn't tied to one provider.
public interface IChatModel
{
    string Complete(string systemPrompt, string userPrompt);
}
