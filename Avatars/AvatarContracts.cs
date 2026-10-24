// Stable contracts for a future offline avatar module. Keep the Guide independent
// from the renderer or asset catalog so this can become a separate DLL later.
internal sealed record AvatarPresentation(string DisplayName, string? ImagePath, string ProviderId, bool IsEditable);

internal interface IAvatarProvider
{
    string Id { get; }
    AvatarPresentation GetPresentation();
}
