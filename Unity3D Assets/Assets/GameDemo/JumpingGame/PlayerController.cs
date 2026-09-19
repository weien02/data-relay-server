using DedicatedServer.UnityFramework;
using UnityEngine;

namespace DedicatedServer.Demo.JumpingGame
{
    public class PlayerController : MonoBehaviour
    {
        private UnityEntity _entity;

        private void Awake()
        {
            this.TryResolveEntity();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!this.TryResolveEntity())
            {
                return;
            }

            this._entity.Publish(new UnityTriggerEnterEvent(other));
        }

        private bool TryResolveEntity()
        {
            if (this._entity is null)
            {
                this._entity = this.GetComponent<UnityEntity>();
            }
            return !(this._entity is null);
        }
    }
}
