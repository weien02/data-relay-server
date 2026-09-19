using System;
using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServer.UnityFramework
{
    public class UnityEntity : MonoBehaviour
    {
        private readonly Dictionary<Type, List<Delegate>> _eventHandlers = new Dictionary<Type, List<Delegate>>();
        private uint _runtimeID;
        public uint RuntimeID => this._runtimeID;
        private UnityEntityManager _entityManager;
        private bool _isDeleted;

        public bool HasAuthority()
        {
            if (this._isDeleted)
            {
                return false;
            }
            return this._entityManager.HasAuthority(this._runtimeID);
        }

        public bool IsTemporaryBackupAuthority()
        {
            if (this._isDeleted)
            {
                return false;
            }
            return this._entityManager.IsTemporaryAuthorityBackup(this._runtimeID);
        }

        public void RequestDeleteNetworkEntity()
        {
            if (this._isDeleted)
            {
                return;
            }
            this._entityManager.RequestDeleteNetworkEntity(this._runtimeID);
        }

        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type eventType = typeof(TEvent);
            List<Delegate> handlers;
            if (!this._eventHandlers.TryGetValue(eventType, out handlers))
            {
                handlers = new List<Delegate>();
                this._eventHandlers.Add(eventType, handlers);
            }
            handlers.Add(handler);
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler is null)
            {
                return;
            }

            Type eventType = typeof(TEvent);
            List<Delegate> handlers;
            if (!this._eventHandlers.TryGetValue(eventType, out handlers))
            {
                return;
            }

            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                this._eventHandlers.Remove(eventType);
            }
        }

        public void Publish<TEvent>(TEvent evt)
        {
            if (this._isDeleted)
            {
                return;
            }

            List<Delegate> handlers;
            if (!this._eventHandlers.TryGetValue(typeof(TEvent), out handlers))
            {
                return;
            }

            for (int i = 0; i < handlers.Count; i++)
            {
                ((Action<TEvent>)handlers[i])(evt);
            }
        }

        internal void MarkDeleted()
        {
            this._isDeleted = true;
            this._eventHandlers.Clear();
        }

        public void Initialize(uint runtimeID, UnityEntityManager entityManager)
        {
            this._runtimeID = runtimeID;
            this._entityManager = entityManager;
            this._isDeleted = false;
        }
    }
}
