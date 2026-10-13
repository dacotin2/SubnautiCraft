using System.Collections.Generic;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Steve mode's Subnautica side, like SkyCraft's PlayerPuppet: the diver's own movement is switched off and he is
    /// moved to Steve every frame, invisible, so Subnautica's world streaming, biomes, depth, oxygen and creatures still
    /// follow "the player". His capsule stays on (kinematic) so creatures can still bite.
    /// </summary>
    internal sealed class RyleyPuppet
    {
        private readonly List<Renderer> hidden = new List<Renderer>();
        private float nextHideScan;
        /// <summary>
        /// The diver's origin relative to his eyes: Subnautica keeps it at his head (it decides "under water" there).
        /// Fixed, not measured when Steve takes over: taken during a cinematic or a ladder it came out a metre and a half
        /// off, and the game then thought the diver was far below Steve (breathing, the lifepod, the depth).
        /// </summary>
        private readonly Vector3 fromEye = new Vector3(0f, -0.05f, 0f);
        private Vector3 lastSet;
        private bool hasLastSet;

        /// <summary>Where the eyes are relative to the diver's origin.</summary>
        public Vector3 EyeOffset => -fromEye;

        public void TakeOver(Camera camera)
        {
            Player player = Player.main;
            hasLastSet = false;
            Freeze(player);
            Hide(player);
        }

        /// <summary>Steve drives a vehicle: Subnautica has the controls, but the diver stays out of sight.</summary>
        public void HideBody()
        {
            if (Player.main != null && Time.unscaledTime >= nextHideScan)
            {
                Hide(Player.main);
            }
        }

        public void ShowBody()
        {
            foreach (Renderer r in hidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            hidden.Clear();
        }

        public void Release()
        {
            Player player = Player.main;
            // back on foot only: in a vehicle or a pilot's seat Subnautica keeps the diver's walking/swimming off itself
            if (player != null && player.playerController != null && player.GetMode() == Player.Mode.Normal
                && player.currentMountedVehicle == null && !player.isPiloting)
            {
                player.playerController.SetEnabled(true);
            }

            foreach (Renderer r in hidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            hidden.Clear();
            hasLastSet = false;
        }

        /// <summary>
        /// Every frame: keep the diver frozen and hidden, and put him where Steve's eyes are. Returns false (and moves
        /// nothing) when Subnautica moved him itself since last frame (a respawn, a warp): Steve has to come here.
        /// </summary>
        public bool Follow(Vector3 steveEye)
        {
            Player player = Player.main;
            Freeze(player);
            if (Time.unscaledTime >= nextHideScan)
            {
                Hide(player); // tools taken out later get hidden too
            }

            if (hasLastSet && (player.transform.position - lastSet).sqrMagnitude > 4f * 4f)
            {
                hasLastSet = false;
                return false;
            }

            lastSet = steveEye + fromEye;
            player.SetPosition(lastSet);
            hasLastSet = true;
            return true;
        }

        /// <summary>After Steve was sent to the diver: wait for him before moving the diver again.</summary>
        public void Forget()
        {
            hasLastSet = false;
        }

        private static void Freeze(Player player)
        {
            PlayerController controller = player.playerController;
            if (controller == null)
            {
                return;
            }

            if (controller.enabled)
            {
                controller.SetEnabled(false); // motors off, rigidbody kinematic
            }

            // the capsule back on (still kinematic) and touchable: switching the motor off also stops the body detecting
            // collisions, and without that no creature can bite (a Reaper's mouth is a trigger) and no trigger volume fires
            Collider capsule = controller.underWaterController != null ? controller.underWaterController.GetCollider() : null;
            if (capsule != null && !capsule.enabled)
            {
                capsule.enabled = true;
            }

            if (player.rigidBody != null && !player.rigidBody.detectCollisions)
            {
                player.rigidBody.detectCollisions = true;
            }
        }

        /// <summary>The diver's body, arms and held tool (not the camera rig, which carries screen effects).</summary>
        private void Hide(Player player)
        {
            nextHideScan = Time.unscaledTime + 1f;
            Transform cameraRig = SNCameraRoot.main != null ? SNCameraRoot.main.transform : null;
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled && (cameraRig == null || !r.transform.IsChildOf(cameraRig)))
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
        }
    }
}
