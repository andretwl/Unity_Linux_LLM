using Unity.Netcode.Components;

namespace NPCSystem
{
    /// <summary>
    /// Owner-authoritative NetworkTransform for player avatars. The owning client moves its
    /// CharacterController locally and Netcode replicates the resulting transform to the server
    /// and other clients.
    /// </summary>
    public class NPCOwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
