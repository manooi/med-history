namespace MedHistory.Models;

public class TypesViewModel
{
    /// <summary>All types, active and not, in display order.</summary>
    public required IReadOnlyList<EntryTypeRow> Types { get; init; }

    /// <summary>Echoed back so a rejected name stays in the input.</summary>
    public string? NewName { get; init; }
}

public class EntryTypeRow
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required bool IsActive { get; init; }

    public required bool IsBuiltIn { get; init; }
}
