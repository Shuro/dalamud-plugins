using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace GobchatEx.Chat;

/// <summary>
/// Resolves a chat message sender's (name, world) pair for group matching. Prefers a structured
/// <see cref="PlayerPayload"/> when the sender carries one — gives exact <c>PlayerName</c>/<c>World</c>
/// directly. Falls back to the raw payload run otherwise: a cross-world sender's world suffix is
/// rendered as an <see cref="IconPayload"/> (<see cref="BitmapFontIcon.CrossWorld"/>, icon 88) between
/// the name and world text runs — confirmed by Dalamud's own SeStringEvaluator, which builds the same
/// shape (<c>AppendIcon(88)</c> then the world row's name) when resolving a cross-world player name.
/// There are no brackets, unlike the standalone app's own chat-log convention for stored trigger
/// entries (see <see cref="GroupMatcher"/>'s "Name [World]" trigger format, which is a config-storage
/// convention only, not something sender text ever contains).
/// </summary>
internal static class SenderIdentity
{
    public static void Resolve(SeString sender, out string name, out string? world)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is not PlayerPayload player)
                continue;

            name = player.PlayerName;
            world = player.World.ValueNullable?.Name.ExtractText();
            return;
        }

        string? nameText = null;
        string? worldText = null;
        var pastCrossWorldIcon = false;

        foreach (var payload in sender.Payloads)
        {
            if (payload is IconPayload { Icon: BitmapFontIcon.CrossWorld })
            {
                pastCrossWorldIcon = true;
                continue;
            }

            if (payload is not TextPayload { Text.Length: > 0 } textPayload)
                continue;

            if (pastCrossWorldIcon)
                worldText = (worldText ?? string.Empty) + textPayload.Text;
            else
                nameText = (nameText ?? string.Empty) + textPayload.Text;
        }

        name = nameText ?? sender.TextValue;
        world = worldText;
    }
}
