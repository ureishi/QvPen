using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvPen.Udon.World
{
    public class PlayerMods : UdonSharpBehaviour
    {
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 4f;
        [SerializeField] private float jumpImpulse = 3f;
        [SerializeField] private float gravityStrength = 1f;
        [SerializeField] private bool useLegacyLocomotion = false;

        private void Start()
        {
            var localPlayer = Networking.LocalPlayer;

            if (localPlayer == null) return;

            localPlayer.SetRunSpeed(runSpeed);
            localPlayer.SetWalkSpeed(walkSpeed);
            localPlayer.SetJumpImpulse(jumpImpulse);
            localPlayer.SetGravityStrength(gravityStrength);
            if (useLegacyLocomotion)
                localPlayer.UseLegacyLocomotion();

            Destroy(gameObject);
        }
    }
}
