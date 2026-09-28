namespace GobchatEx.Core;

/// <summary>
/// Completes a chat sender that resolved without a world, shared by the native chat pass and the
/// Chat 2 style provider. The local player's own posts carry no PlayerPayload (you aren't an
/// interactable sender to yourself), so they resolve to raw text — no world, possibly a
/// party-number prefix — and a world-qualified group member would never match: substitute the
/// local player's clean name and home world. Any other world-less sender stands on the current
/// world (visitors always render with a cross-world suffix).
/// </summary>
public static class SenderCompletion
{
    public static (string Name, string? World) Complete(
        string name, string? world, bool isSelf,
        string localName, string? localHomeWorld, string? localCurrentWorld)
    {
        if (world != null)
            return (name, world);

        if (isSelf && localName.Length > 0)
            return (localName, localHomeWorld ?? localCurrentWorld);

        return (name, localCurrentWorld);
    }
}
