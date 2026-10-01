namespace StreamingPanel.Core.Entities;

/// <summary>A postal address belonging to a <see cref="Person"/>.</summary>
public class Address
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string Street { get; set; } = default!;
    public string City { get; set; } = default!;
    public string State { get; set; } = default!;
    public string ZipCode { get; set; } = default!;

    public Person? Person { get; set; }
}
