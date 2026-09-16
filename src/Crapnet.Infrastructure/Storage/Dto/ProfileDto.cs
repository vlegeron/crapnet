namespace Crapnet.Infrastructure.Storage.Dto;

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Rules.Profile"/>.</summary>
internal sealed class ProfileDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<RuleDto>? Rules { get; set; }
}

/// <summary>On-disk shape of <see cref="Crapnet.Domain.Rules.Rule"/>.</summary>
internal sealed class RuleDto
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public MatchCriteriaDto? Match { get; set; }
    public ImpairmentSetDto? Impairments { get; set; }
}

/// <summary>
/// On-disk shape of <see cref="Crapnet.Domain.Rules.MatchCriteria"/>.
/// </summary>
/// <remarks>
/// Selectors are stored as the expression the user typed rather than as an expanded list of
/// ranges. That keeps the file readable and reviewable in a diff, and it means a profile
/// round-trips through the editor showing <c>10.0.0.0/8</c> instead of <c>10.0.0.0-10.255.255.255</c>.
/// </remarks>
internal sealed class MatchCriteriaDto
{
    public string? Direction { get; set; }
    public string? Protocol { get; set; }
    public string? DeviceAddresses { get; set; }
    public string? RemoteAddresses { get; set; }
    public string? DevicePorts { get; set; }
    public string? RemotePorts { get; set; }
}
