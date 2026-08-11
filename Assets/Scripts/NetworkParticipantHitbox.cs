using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Maps a child collider back to its replicated participant root.</summary>
public sealed class NetworkParticipantHitbox : ScriptBehaviour
{
    [SerializedField] private GameObject? participant = null;

    public uint ParticipantEntityId =>
        participant is not null && participant.IsValid ? participant.EntityId : 0;
}
