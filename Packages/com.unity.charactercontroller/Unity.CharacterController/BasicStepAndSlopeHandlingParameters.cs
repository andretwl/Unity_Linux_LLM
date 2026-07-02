using System;

namespace Unity.CharacterController
{
    /// <summary>
    /// Contains a set of parameters related to step and slope handling behaviour for a character
    /// </summary>
    [Serializable]
    public struct BasicStepAndSlopeHandlingParameters
    {
        /// <summary>
        /// Whether or not step handling logic is enabled
        /// </summary>
        [UnityEngine.Header("Step Handling")]
        [UnityEngine.Tooltip("Whether or not step handling logic is enabled")]
        public bool StepHandling;
        /// <summary>
        /// Max height that the character can step on
        /// </summary>
        [UnityEngine.Tooltip("Max height that the character can step on")]
        public float MaxStepHeight;
        /// <summary>
        /// Horizontal offset distance of extra downwards raycasts used to detect grounding around a step
        /// </summary>
        [UnityEngine.Tooltip("Horizontal offset distance of extra downwards raycasts used to detect grounding around a step")]
        public float ExtraStepChecksDistance;
        /// <summary>
        /// Character width used to determine grounding for steps. For a capsule this should be 2x capsule radius, and for a box it should be maximum box width. This is for cases where character with a spherical base tries to step onto an angled surface that is near the character's max step height. In thoses cases, the character might be grounded on steps on one frame, but wouldn't be grounded on the next frame as the spherical nature of its shape would push it a bit further up beyond its max step height.
        /// </summary>
        [UnityEngine.Tooltip("Character width used to determine grounding for steps. For a capsule this should be 2x capsule radius, and for a box it should be maximum box width. This is for cases where character with a spherical base tries to step onto an angled surface that is near the character's max step height. In thoses cases, the character might be grounded on steps on one frame, but wouldn't be grounded on the next frame as the spherical nature of its shape would push it a bit further up beyond its max step height.")]
        public float CharacterWidthForStepGroundingCheck;

        /// <summary>
        /// Whether or not to cancel grounding when the character is moving off a ledge. This prevents the character from "snapping" onto the ledge as it moves off of it
        /// </summary>
        [UnityEngine.Header("Slope Changes")]
        [UnityEngine.Tooltip("Whether or not to cancel grounding when the character is moving off a ledge. This prevents the character from \"snapping\" onto the ledge as it moves off of it")]
        public bool PreventGroundingWhenMovingTowardsNoGrounding;
        /// <summary>
        /// Whether or not the character has a max slope change that it can stay grounded on
        /// </summary>
        [UnityEngine.Tooltip("Whether or not the character has a max slope change that it can stay grounded on")]
        public bool HasMaxDownwardSlopeChangeAngle;
        /// <summary>
        /// Max slope change that the character can stay grounded on
        /// </summary>
        [UnityEngine.Tooltip("Max slope change that the character can stay grounded on (degrees)")]
        [UnityEngine.Range(0f, 180f)]
        public float MaxDownwardSlopeChangeAngle;

        /// <summary>
        /// Whether or not to constrain the character velocity to ground plane when it hits a non-grounded slope
        /// </summary>
        [UnityEngine.Header("Misc")]
        [UnityEngine.Tooltip("Whether or not to constrain the character velocity to ground plane when it hits a non-grounded slope")]
        public bool ConstrainVelocityToGroundPlane;

        /// <summary>
        /// Gets a default initialized version of step and slope handling parameters
        /// </summary>
        /// <returns> Default parameters struct </returns>
        public static BasicStepAndSlopeHandlingParameters GetDefault()
        {
            return new BasicStepAndSlopeHandlingParameters
            {
                StepHandling = false,
                MaxStepHeight = 0.5f,
                ExtraStepChecksDistance = 0.1f,
                CharacterWidthForStepGroundingCheck = 1f,

                PreventGroundingWhenMovingTowardsNoGrounding = true,
                HasMaxDownwardSlopeChangeAngle = false,
                MaxDownwardSlopeChangeAngle = 90f,

                ConstrainVelocityToGroundPlane = true,
            };
        }
    }
}
