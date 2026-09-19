using UnityEngine;

namespace DedicatedServer.UnityFramework
{
    public readonly struct UnityTriggerEnterEvent
    {
        public readonly Collider collider;

        public UnityTriggerEnterEvent(Collider collider)
        {
            this.collider = collider;
        }
    }
}
