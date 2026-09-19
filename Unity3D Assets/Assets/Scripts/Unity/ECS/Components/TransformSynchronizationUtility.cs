using DedicatedServer.UnityFramework.ECS.Data;
using UnityEngine;

namespace DedicatedServer.UnityFramework.ECS.Components
{
    internal static class TransformSynchronizationUtility
    {
        public static void ApplyToTransform(TransformData transformData, Transform targetTransform)
        {
            targetTransform.position = transformData.GetPosition();
            targetTransform.rotation = Quaternion.Euler(transformData.GetRotation());
            targetTransform.localScale = transformData.GetScale();
        }

        public static void WriteToDataStore(TransformData transformData, Transform sourceTransform)
        {
            WriteToDataStore(transformData, sourceTransform.position, sourceTransform.rotation.eulerAngles, sourceTransform.localScale);
        }

        public static void WriteToDataStore(TransformData transformData, Vector3 position, Vector3 rotationEuler, Vector3 scale)
        {
            transformData.SetPosition(position);
            transformData.SetRotation(rotationEuler);
            transformData.SetScale(scale);
        }
    }
}
