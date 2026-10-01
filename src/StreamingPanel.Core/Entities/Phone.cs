namespace StreamingPanel.Core.Entities;

/// <summary>A phone belonging to a <see cref="Person"/>.</summary>
public class Phone
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string Number { get; set; } = default!;
    public string Type { get; set; } = default!;

    public Person? Person { get; set; }
}
