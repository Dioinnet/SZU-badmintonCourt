namespace SzuBadminton.Models;

public sealed class SavedAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StudentId { get; set; } = string.Empty;
    public string EncryptedPassword { get; set; } = string.Empty;
}
