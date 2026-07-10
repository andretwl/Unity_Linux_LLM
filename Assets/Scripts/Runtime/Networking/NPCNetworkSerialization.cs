using Unity.Netcode;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Registers <c>string</c> serialization with NGO so that
    /// <see cref="NetworkVariable{T}"/> works without source-generator
    /// codegen.  Called once before scene load; identical static-constructor
    /// registrations in individual <see cref="NetworkBehaviour"/> types
    /// have been removed in favour of this single point of setup.
    /// </summary>
    public static class NPCNetworkSerialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            if (UserNetworkVariableSerialization<string>.WriteValue != null)
                return; // already registered — idempotent guard

            UserNetworkVariableSerialization<string>.WriteValue =
                (FastBufferWriter writer, in string value) =>
                {
                    writer.WriteValueSafe(value);
                };

            UserNetworkVariableSerialization<string>.ReadValue =
                (FastBufferReader reader, out string value) =>
                {
                    reader.ReadValueSafe(out value);
                };

            UserNetworkVariableSerialization<string>.DuplicateValue =
                (in string value, ref string duplicatedValue) =>
                {
                    duplicatedValue = value;
                };

            Debug.Log(
                "[NPCNetworkSerialization] Registered NetworkVariable<string> serialization."
            );
        }
    }
}
