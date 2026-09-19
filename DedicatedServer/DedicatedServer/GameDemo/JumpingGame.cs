using DedicatedServer.Framework;
using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Server;
using DedicatedServer.UnityFramework.ECS.Data;
using System;
#if UNITY_2017_1_OR_NEWER
using DedicatedServer.Framework.Client;
using DedicatedServer.UnityFramework;
using DedicatedServer.UnityFramework.ECS.Components;
using UnityEngine;
#endif

namespace DedicatedServer.Demo.JumpingGame
{
#if UNITY_2017_1_OR_NEWER
    public class JumpingGame : MonoBehaviour
#else
    public class JumpingGame
#endif
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<JumpingGame>();
        private GameServer _gameServer;
#if UNITY_2017_1_OR_NEWER
        private GameClient _gameClient;
        private ClientSideEntityManager _entityManager;
        private UnityEntityManager _unityEntityManager;
#endif
        public readonly Configurations configurations = Configurations.GetDefaultConfigurations();

        private void ConfigureFramework()
        {
            this.configurations.DisconnectThersholdFrameCount = 100;
            this.configurations.EnableParallelWriteTickLogging = false;
            this.configurations.EnableDirtyOnlySync = true;
            Configurations.SetGlobalConfigurations(this.configurations);
            EntityRegistry.SetDataStores(0, new Type[]
            {
                typeof(TransformData),
                typeof(AvatarJumpStateData),
                typeof(AvatarRespawnData),
            });
        }

#if UNITY_2017_1_OR_NEWER
        private void Awake()
        {
            this.ConfigureFramework();
            LoggerUtil.SetOutputMethod(UnityLogger.OutputMethod);
            this._unityEntityManager = GameObject.Find(UnityEntityComponentBase.GameObjectName_UnityEntityManager).GetComponent<UnityEntityManager>();
            EntityRegistry.SetNetComponents(0, new Type[]
            {
                typeof(AvatarInputComponent),
                typeof(AvatarPresentationComponent),
                typeof(AvatarPhysicsComponent),
                typeof(AvatarBackupCleanupComponent),
                typeof(AvatarTransformSyncComponent),
            });
            this._unityEntityManager.RegisterEntity(0, "/jumpinggame", "Player");
            this._unityEntityManager.SetEntityPrefabLoader((string assetBundlePath, string prefabName) => ResourceManager.LoadResource<GameObject>(assetBundlePath, prefabName));
            Time.timeScale = 2;
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            this._gameServer?.Tick(deltaTime);
            this._gameClient?.Tick(deltaTime);
            this._unityEntityManager.Tick(deltaTime);
        }
#else
        public void Initialize()
        {
            this.ConfigureFramework();
        }

        public void Update(float deltaTime)
        {
            this._gameServer?.Tick(deltaTime);
        }
#endif

        public void StartServer(ushort listeningPort)
        {
            _logger.Log("begin StartServer");
            this._gameServer = new GameServer();
            this._gameServer.StartServer(listeningPort);
            this._gameServer.StartFrameSync();
#if !UNITY_2017_1_OR_NEWER
            _logger.Log("server running on port " + listeningPort);
#endif
            _logger.Log("end StartServer");
        }

#if UNITY_2017_1_OR_NEWER
        public void StartClient(uint clientID, string serverIPAddress, ushort serverPort)
        {
            _logger.Log("begin StartClient");
            this._gameClient = new GameClient(clientID);
            this._gameClient.Connect(serverIPAddress, serverPort);
            this._gameClient.OnConnectedToServer = () => this._gameClient.Login();
            this._gameClient.OnLoggedIn = this.OnLoggedIn;
            _logger.Log("end StartClient");
        }

        private void OnLoggedIn(bool shouldRecoverLocalPlayerEntities)
        {
            GameObject.Find("Canvas/MainMenu").SetActive(false);
            this._entityManager = this._gameClient.ClientSideEntityManager;
            this._unityEntityManager.SetEntityManager(this._entityManager);
            if (shouldRecoverLocalPlayerEntities)
            {
                this._unityEntityManager.RequestAcquireLocalPlayerEntityAuthorities();
            }
            else
            {
                this._unityEntityManager.RequestCreateNetworkEntityWithAuthority(0, EntityAuthorityType.Full_LocalPlayer);
            }
        }
#endif

        public void Dispose()
        {
            _logger.Log("begin Dispose");
            _logger.LogException(() => this._gameServer?.Dispose());
#if UNITY_2017_1_OR_NEWER
            _logger.LogException(() => this._gameClient?.Dispose());
#endif
            _logger.Log("end Dispose");
        }

#if UNITY_2017_1_OR_NEWER
        private void OnDestroy()
        {
            this.Dispose();
        }
#endif
    }
}
