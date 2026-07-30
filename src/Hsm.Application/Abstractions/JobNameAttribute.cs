namespace Hsm.Application.Abstractions;

/// <summary>Stable queue name for a command. Job payloads never carry CLR type names —
/// the name→type map is built at startup by the job registry.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JobNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
