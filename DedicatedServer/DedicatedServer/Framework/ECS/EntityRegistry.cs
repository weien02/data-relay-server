using System;
using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework.ECS
{
    public static class EntityRegistry
    {
        private class RegisteredEntity
        {
            public readonly int registryID;
            public Type[] netComponents;
            public Type[] dataStores;

            public RegisteredEntity(int registryID)
            {
                this.registryID = registryID;
            }
        }

        private static Dictionary<int, RegisteredEntity> _entities = new Dictionary<int, RegisteredEntity>();

        private static RegisteredEntity GetOrCreate(int registryID)
        {
            RegisteredEntity entity;
            if(!_entities.TryGetValue(registryID, out entity))
            {
                entity = new RegisteredEntity(registryID);
                _entities.Add(registryID, entity);
            }
            return entity;
        }

        private static RegisteredEntity Get(int registryID)
        {
            RegisteredEntity entity;
            if (!_entities.TryGetValue(registryID, out entity))
            {
                throw new Exception("There is no entity with the registry id " + registryID);
            }
            return entity;
        }

        /// <summary>
        /// Use this method to bind the entity components onto an entity type with the registry id.
        /// This should only be called in client, because all game logical code run in client.
        /// </summary>
        /// <param name="registryID"></param>
        /// <param name="netComponents"></param>
        public static void SetNetComponents(int registryID, Type[] netComponents)
        {
            RegisteredEntity entity = GetOrCreate(registryID);
            entity.netComponents = netComponents;
        }

        /// <summary>
        /// Use this method to bind the entity data stores onto an entity type with the registry id.
        /// This should be called in both client and dedicated server code.
        /// </summary>
        /// <param name="registryID"></param>
        /// <param name="dataStores"></param>
        public static void SetDataStores(int registryID, Type[] dataStores)
        {
            RegisteredEntity entity = GetOrCreate(registryID);
            entity.dataStores = dataStores;
        }

        public static IEnumerator GetNetComponents(int registryID)
        {
            RegisteredEntity entity = Get(registryID);
            return entity.netComponents.GetEnumerator();
        }

        public static IEnumerator GetDataStores(int registryID)
        {
            RegisteredEntity entity = Get(registryID);
            return entity.dataStores.GetEnumerator();
        }
    }
}
