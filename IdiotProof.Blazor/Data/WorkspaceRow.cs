using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IdiotProof.Blazor.Data;

[Table("Workspaces")]
public sealed class WorkspaceRow
{
    [Key]
    [MaxLength(64)]
    public string WorkspaceId { get; set; } = string.Empty;

    [MaxLength(450)]
    public string OwnerUserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string BodyJson { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
