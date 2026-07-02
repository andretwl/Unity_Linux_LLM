# Character update

This package, at its core, only provides a toolbag of various character controller utilities that you can use in order to build highly customizable character controllers. These utilities are available in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities). This means there is no "built-in" way to update the character controller; how you handle the character update is your decision to make.

However, the package provides the "Standard Characters" sample (downloadable via the "Samples" tab of the character controller package in the Package Manager), in order to show you a good recommended starting point for how to handle updating the character controller. This section will go over how a typical charater update is handled, as shown in these Standard Characters.

You may refer to the [Standard characters implementation overview](get-started-implementation-overview.md) for a more detailed explanation of the Standard Character architecture.


## The character physics update steps

These are the main recommended steps for a typical character physics update, which handles character velocity and collision solving. The character physics update would typically be called at a fixed time step, inside the [`KinematicCharacterPhysicsUpdateGroup`](xref:Unity.CharacterController.KinematicCharacterPhysicsUpdateGroup). These steps are available in available in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities):

|**Method**|**Description**|
|---|---|
|[`Update_Initialize`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_Initialize*)| Clears and initializes core character data and buffers at the start of the update.|
|[`Update_ParentMovement`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ParentMovement*)| Moves the character based on its assigned [`ParentEntity`](xref:Unity.CharacterController.KinematicCharacterBody.ParentEntity), if any.|
|[`Update_Grounding`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_Grounding*)| Detects character grounding.|
|[`Update_PreventGroundingFromFutureSlopeChange`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_PreventGroundingFromFutureSlopeChange*)| Cancels a character's grounded status based on the definitions in the [`BasicStepAndSlopeHandlingParameters`](xref:Unity.CharacterController.BasicStepAndSlopeHandlingParameters) given to the method. For example, cancel grounding if the character is heading towards a ledge.|
|[`Update_GroundPushing`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_GroundPushing*)| Applies a constant force to the current ground entity, if the entity is dynamic.|
|[`Update_MovementAndDecollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_MovementAndDecollisions*)| Moves the character with its velocity and solves collisions.|
|[`Update_MovingPlatformDetection`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_MovingPlatformDetection*)| Detects valid moving platform entities, and assigns them as the character's `ParentEntity`. For more information, see the documentation on [Parenting](concepts-parenting.md).|
|[`Update_ParentMomentum`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ParentMomentum*)| Preserves the velocity momentum when a character is detached from a parent body.|
|[`Update_ProcessStatefulCharacterHits`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ProcessStatefulCharacterHits*)| Fills the `StatefulKinematicCharacterHit` buffer on the character entity with character hits that have an `Enter`, `Exit`, or `Stay` state.|


## Character processor callbacks

The character processor is a user-implemented struct that implements the [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor) interface. Its main purpose is to provide a way to customize the logic executed by the update steps in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities), via several "callbacks". In the Standard Characters, an initial version of this processor is already created, and it is meant to be customized by users.

The character processor is able to customize the character controller logic by passing itself as a parameter to [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities) methods. These methods then call functions on the character processor in various "callbacks", where users can modify the default implementation to suit their needs.

In summary, systems will schedule jobs iterating on the processor aspect implementing [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor), which will then call character update steps from [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities), which will then call back to functions of [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor).

The following character processor methods can be used to customize character logic. For full descriptions of each of these methods see the [`IKinematicCharacterProcessor` API documentation](xref:Unity.CharacterController.IKinematicCharacterProcessor`1):

|**Method**|**Description**|
|---|---|
|`UpdateGroundingUp`| Updates the up direction that a character compares slope normals with. You must write this direction to `KinematicCharacterBody.GroundingUp`. You can use the default implementation in [`KinematicCharacterUtilities.Default_UpdateGroundingUp`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_UpdateGroundingUp*).|
|`CanCollideWithHit`| Checks if the character can collide with a hit. Returns true if the character can collide, and false otherwise.|
|`OnMovementHit`| Determines what happens when the character collider casts have detected a hit during the movement iterations. By default, this should call [`KinematicCharacterUtilities.Default_OnMovementHit`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_OnMovementHit*).|
|`IsGroundedOnHit`| Determines if the character is grounded on the hit or not. By default, it calls [`KinematicCharacterUtilities.Default_IsGroundedOnHit`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_IsGroundedOnHit*), which checks the slope angle and velocity direction to determine the final result.|
|`OverrideDynamicHitMasses`| Modifies the mass ratios between the character and another dynamic body when they collide. This is only called for characters that have `KinematicCharacterProperties.SimulateDynamicBody` set to true. You can leave this method empty if you don't want to modify the mass ratio.|
|`ProjectVelocityOnHits`| Determines how the character velocity gets projected on hits, based on all hits so far this frame. By default, you should call [`KinematicCharacterUtilities.Default_ProjectVelocityOnHits`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_ProjectVelocityOnHits*) here. You shouldn't need to change this callback, unless you want to make your character bounce on certain surfaces, for example.|


## Other character utilities

`KinematicCharacterUtilities` also contains various methods for querying the world with physics casting and collision filtering. For example:

* [`CastColliderClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CastColliderClosestCollisions*)
* [`CastColliderAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CastColliderAllCollisions*)
* [`RaycastClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.RaycastClosestCollisions*)
* [`RaycastAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.RaycastAllCollisions*)
* [`CalculateDistanceClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CalculateDistanceClosestCollisions*)
* [`CalculateDistanceAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CalculateDistanceAllCollisions*)

You may use these utilities to implement additional character controller features in your own code.

















## KinematicCharacterUtilities

[`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities) is a static class that contains the core implementation of the character controller logic. **This is the recommended approach** for implementing character controllers. All the character update steps, physics queries, and utility functions are implemented here.

These are the main steps for a typical character update, available in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities):

|**Method**|**Description**|
|---|---|
|[`Update_Initialize`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_Initialize*)| Clears and initializes core character data and buffers at the start of the update.|
|[`Update_ParentMovement`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ParentMovement*)| Moves the character based on its assigned [`ParentEntity`](xref:Unity.CharacterController.KinematicCharacterBody.ParentEntity), if any.|
|[`Update_Grounding`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_Grounding*)| Detects character grounding.|
|[`Update_PreventGroundingFromFutureSlopeChange`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_PreventGroundingFromFutureSlopeChange*)| Cancels a character's grounded status based on the definitions in the [`BasicStepAndSlopeHandlingParameters`](xref:Unity.CharacterController.BasicStepAndSlopeHandlingParameters) given to the method. For example, cancel grounding if the character is heading towards a ledge.|
|[`Update_GroundPushing`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_GroundPushing*)| Applies a constant force to the current ground entity, if the entity is dynamic.|
|[`Update_MovementAndDecollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_MovementAndDecollisions*)| Moves the character with its velocity and solves collisions.|
|[`Update_MovingPlatformDetection`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_MovingPlatformDetection*)| Detects valid moving platform entities, and assigns them as the character's `ParentEntity`. For more information, see the documentation on [Parenting](concepts-parenting.md).|
|[`Update_ParentMomentum`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ParentMomentum*)| Preserves the velocity momentum when a character is detached from a parent body.|
|[`Update_ProcessStatefulCharacterHits`](xref:Unity.CharacterController.KinematicCharacterUtilities.Update_ProcessStatefulCharacterHits*)| Fills the `StatefulKinematicCharacterHit` buffer on the character entity with character hits that have an `Enter`, `Exit`, or `Stay` state.|

The standard characters that come with the package already take care of calling these in the correct order and at the correct time, in order to solve character physics.

The methods in `KinematicCharacterUtilities` are organized into several regions:

* **Kinematic Character Update Steps**: The main character update methods like `Update_Initialize`, `Update_Grounding`, `Update_MovementAndDecollisions`, etc.
* **Kinematic Character Default Processor Callbacks**: Default implementations for processor callbacks like `Default_IsGroundedOnHit`, `Default_ProjectVelocityOnHits`, etc.
* **Kinematic Character Public Utilities**: Public utility methods for raycast, collider casting, distance calculations, and other physics queries.
* **Kinematic Character Internal Utilities**: Internal methods for hit filtering, grounding detection, overlap resolution, and other core character controller functionality.

`KinematicCharacterUtilities` also contains various methods for querying the world with physics casting and collision filtering. For example:

* [`CastColliderClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CastColliderClosestCollisions*)
* [`CastColliderAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CastColliderAllCollisions*)
* [`RaycastClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.RaycastClosestCollisions*)
* [`RaycastAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.RaycastAllCollisions*)
* [`CalculateDistanceClosestCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CalculateDistanceClosestCollisions*)
* [`CalculateDistanceAllCollisions`](xref:Unity.CharacterController.KinematicCharacterUtilities.CalculateDistanceAllCollisions*)


## The character aspect (Backwards Compatibility)

The [`KinematicCharacterAspect`](xref:Unity.CharacterController.KinematicCharacterAspect) is **maintained for backwards compatibility only**. The recommended approach is to use [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities) directly.

The [`KinematicCharacterAspect`](xref:Unity.CharacterController.KinematicCharacterAspect) holds all the data required for character updates, and provides wrapper methods that internally call the corresponding methods in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities). These wrapper methods are provided for backwards compatibility with existing code.




## The character processor

The character processor is a user-implemented aspect that implements the [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor) interface. It serves two main purposes:

* Provide a way to customize the logic executed by the update steps in [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities).
* Provide a way for users to define additional components that can be accessed during the character updates.

In the standard characters, an initial version of this processor aspect is already created, and it is meant to be customized by users. Both the fixed character physics update and the character variable update use this processor aspect in order to gain access to all of the data and methods that they could require.

The character processor is able to customize the character controller logic by passing itself as a parameter to [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities) methods. These methods then call functions on the character processor in various "callbacks", where users can modify the default implementation to suit their needs.

In summary, systems will schedule jobs iterating on the processor aspect implementing [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor), which will then call character update steps from [`KinematicCharacterUtilities`](xref:Unity.CharacterController.KinematicCharacterUtilities), which will then call back to functions of [`IKinematicCharacterProcessor`](xref:Unity.CharacterController.IKinematicCharacterProcessor).

The following character processor methods can be used to customize character logic. For full descriptions of each of these methods see the [`IKinematicCharacterProcessor` API documentation](xref:Unity.CharacterController.IKinematicCharacterProcessor`1):

|**Method**|**Description**|
|---|---|
|`UpdateGroundingUp`| Updates the up direction that a character compares slope normals with. You must write this direction to `KinematicCharacterBody.GroundingUp`. You can use the default implementation in [`KinematicCharacterUtilities.Default_UpdateGroundingUp`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_UpdateGroundingUp*).|
|`CanCollideWithHit`| Checks if the character can collide with a hit. Returns true if the character can collide, and false otherwise.|
|`OnMovementHit`| Determines what happens when the character collider casts have detected a hit during the movement iterations. By default, this should call [`KinematicCharacterUtilities.Default_OnMovementHit`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_OnMovementHit*).|
|`IsGroundedOnHit`| Determines if the character is grounded on the hit or not. By default, it calls [`KinematicCharacterUtilities.Default_IsGroundedOnHit`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_IsGroundedOnHit*), which checks the slope angle and velocity direction to determine the final result.|
|`OverrideDynamicHitMasses`| Modifies the mass ratios between the character and another dynamic body when they collide. This is only called for characters that have `KinematicCharacterProperties.SimulateDynamicBody` set to true. You can leave this method empty if you don't want to modify the mass ratio.|
|`ProjectVelocityOnHits`| Determines how the character velocity gets projected on hits, based on all hits so far this frame. By default, you should call [`KinematicCharacterUtilities.Default_ProjectVelocityOnHits`](xref:Unity.CharacterController.KinematicCharacterUtilities.Default_ProjectVelocityOnHits*) here. You shouldn't need to change this callback, unless you want to make your character bounce on certain surfaces, for example.|
