
# Dynamic Frustum Culling System For Unity3D


![Dynamic+Frustum+Culling+System](https://github.com/user-attachments/assets/5b35a9b0-f71c-4fd5-8c80-5ff565fcaca8)

One of the key challenges for mobile development lies in rendering thousands of dynamic objects. This puts immense strain on the CPU, responsible for sending all that data to the GPU for rendering.

While Unity's built-in occlusion culling offers valuable performance improvements, it primarily benefits static objects. Dynamic batching can work occasionally, but merging meshes often leads to CPU overload as well.

The most efficient solution in such scenarios involves rendering only the objects within the camera frustum, the visible portion of the scene. To achieve this, I've developed a system that leverages multi-threading using Task.Run and ConcurrentBag for parallel processing. This innovative approach significantly enhances culling performance in large, dynamic scenes.

This system is freely available for integration into your projects, allowing you to optimize resource allocation and provide a smoother experience for both the CPU and GPU.

## How To Use

Import the class named DynamicFrustumCulling.CS into your assets folder

```
Apply this on the parent game object's transform under which all of the dynamic objects are placed
The class will fetch the mesh renderers automatically

Adjust these two variables -->

[Header("Object References")]
[SerializeField] private Transform playerTransform;
[SerializeField] private string referenceObjectTag = "Blackhole";

<--

You can assign player transform manually or give it a player tag so it can fetch player transform.

```
