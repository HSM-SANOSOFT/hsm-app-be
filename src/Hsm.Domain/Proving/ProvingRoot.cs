namespace Hsm.Domain.Proving;

/// <summary>
/// Throwaway proving aggregate (rewrite plan U9): exists to prove the
/// repository port shape and EF Core change tracking — removing a child
/// through the aggregate must delete the row, not orphan it. Removed when
/// the first real module lands.
/// </summary>
public class ProvingRoot
{
    private readonly List<ProvingItem> _items = [];

    private ProvingRoot()
    {
        Name = string.Empty;
    }

    public ProvingRoot(Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public IReadOnlyCollection<ProvingItem> Items => _items;

    public ProvingItem AddItem(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var item = new ProvingItem(Guid.NewGuid(), label);
        _items.Add(item);
        return item;
    }

    public void RemoveItem(Guid itemId)
    {
        var item = _items.SingleOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of aggregate {Id}.");
        _items.Remove(item);
    }
}

public class ProvingItem
{
    private ProvingItem()
    {
        Label = string.Empty;
    }

    internal ProvingItem(Guid id, string label)
    {
        Id = id;
        Label = label;
    }

    public Guid Id { get; private set; }

    public string Label { get; private set; }
}
