using DedicatedServer.Framework;
using DedicatedServer.Framework.Client;
using DedicatedServer.Framework.ECS;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServer.UnityFramework
{
    public class UnityEntityManager : MonoBehaviour, ITickable
    {
        public delegate GameObject EntityPrefabLoaderDelegate(string assetBundlePath, string prefabName);

        private class EntityRegistryData
        {
            public readonly int entityRegistryID;
            public readonly string entityPrefabAssetBundlePath;
            public readonly string entityPrefabName;

            public EntityRegistryData(int entityRegistryID, string entityPrefabAssetBundlePath, string entityPrefabName)
            {
                this.entityRegistryID = entityRegistryID;
                this.entityPrefabAssetBundlePath = entityPrefabAssetBundlePath;
                this.entityPrefabName = entityPrefabName;
            }
        }

        private readonly Dictionary<int, EntityRegistryData> _entityRegistry;
        private ClientSideEntityManager _entityManager;
        private readonly byte[] _staticCustomDataBuffer;
        private DictionaryList<uint, UnityEntity> _entities;
        private EntityPrefabLoaderDelegate _entityPrefabLoader;

        public UnityEntityManager()
        {
            this._entityRegistry = new Dictionary<int, EntityRegistryData>();
            this._entityManager = null;
            this._staticCustomDataBuffer = new byte[sizeof(int)];
            this._entities = new DictionaryList<uint, UnityEntity>();
            this._entityPrefabLoader = null;
        }

        /// <summary>
        /// Use this method to register an entity with its Unity3D prefab information.
        /// </summary>
        /// <param name="entityRegistryID"></param>
        /// <param name="entityPrefabAssetBundlePath"></param>
        /// <param name="entityPrefabName"></param>
        public void RegisterEntity(int entityRegistryID, string entityPrefabAssetBundlePath, string entityPrefabName)
        {
            EntityRegistryData entityRegistryData = new EntityRegistryData(entityRegistryID, entityPrefabAssetBundlePath, entityPrefabName);
            this._entityRegistry.Add(entityRegistryID, entityRegistryData);

        }

        public void SetEntityManager(ClientSideEntityManager entityManager)
        {
            this._entityManager = entityManager;
            this._entityManager.CreateNetworkEntityHandler = this.CreateNetworkEntityHandler;
            this._entityManager.DeleteNetworkEntityHandler = this.DeleteNetworkEntityHandler;
        }

        public void SetEntityPrefabLoader(EntityPrefabLoaderDelegate entityPrefabLoader)
        {
            this._entityPrefabLoader = entityPrefabLoader;
        }

        public void RequestCreateNetworkEntityWithAuthority(int entityRegistryID, EntityAuthorityType authorityType)
        {
            BitConverter.TryWriteBytes(new Span<byte>(this._staticCustomDataBuffer, 0, sizeof(int)), entityRegistryID);
            this._entityManager.RequestCreateNetworkEntityWithAuthority(entityRegistryID, authorityType, this._staticCustomDataBuffer);
        }

        public void RequestAcquireLocalPlayerEntityAuthorities()
        {
            this._entityManager.RequestAcquireLocalPlayerEntityAuthorities();
        }

        public void RequestDeleteNetworkEntity(uint runtimeID)
        {
            this._entityManager.RequestDeleteNetworkEntity(runtimeID);
        }

        private void CreateNetworkEntityHandler(uint runtimeID, byte[] staticCustomDataBuffer, int staticCustomDataLength)
        {
            int registryID = BitConverter.ToInt32(staticCustomDataBuffer, 0);
            EntityRegistryData entityRegistryData = this._entityRegistry[registryID];
            if (this._entityPrefabLoader == null)
            {
                throw new Exception("UnityEntityManager entity prefab loader is not set.");
            }
            GameObject entityPrefab = this._entityPrefabLoader(entityRegistryData.entityPrefabAssetBundlePath, entityRegistryData.entityPrefabName);
            if (entityPrefab == null)
            {
                throw new Exception("Failed to load entity prefab. assetBundlePath: " + entityRegistryData.entityPrefabAssetBundlePath + ", prefabName: " + entityRegistryData.entityPrefabName);
            }
            GameObject entityGameObject = GameObject.Instantiate(entityPrefab, Vector3.zero, Quaternion.identity);
            UnityEntity entity = entityGameObject.AddComponent<UnityEntity>();
            entity.Initialize(runtimeID, this);
            this._entities.Add(runtimeID, entity);
        }

        private void DeleteNetworkEntityHandler(uint runtimeID)
        {
            UnityEntity entity = this._entities.Get(runtimeID);
            if (entity is null)
            {
                return;
            }

            this._entities.Remove(runtimeID);
            entity.MarkDeleted();
            GameObject.Destroy(entity.gameObject);
        }

        public UnityEntity GetEntity(uint runtimeID)
        {
            return this._entities.Get(runtimeID);
        }

        public void Tick(float deltaTime)
        {
            this._entityManager?.Tick(deltaTime);
        }

        public bool HasAuthority(uint runtimeID)
        {
            return this._entityManager.HasAuthority(runtimeID);
        }

        public bool IsTemporaryAuthorityBackup(uint runtimeID)
        {
            return this._entityManager.IsTemporaryAuthorityBackup(runtimeID);
        }
    }
}
