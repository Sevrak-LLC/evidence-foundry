namespace EvidenceFoundry.Models;

public class CaseContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CaseArea { get; set; } = string.Empty;
    public string MatterType { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
}
