namespace RaLaba.API.Security;

public sealed class SupabaseAuthOptions
{
    public string Url { get; set; } = "";
    public string PublishableKey { get; set; } = "";
    public Guid[] StaffUserIds { get; set; } = Array.Empty<Guid>();
}
