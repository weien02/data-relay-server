namespace DedicatedServer.Framework.Networking
{
    internal enum ClientToServerPacketTypes : ushort
    {
        LoginRequest,
        CreateEntityWithAuthorityRequest,
        DeleteEntityRequest,
        AcquireLocalPlayerEntityAuthorityRequest,
        SyncLostRequest,
        SyncEntityData,
        ClientFrameSyncHeartbeat,
        EntityAuthorityBackupCandidateReply,
        CustomPacket,
    }

    internal enum ServerToClientPacketTypes : ushort
    {
        LoginResponse,
        SyncFrameBegin,
        SyncLostResponseBegin,
        SyncEntityCreation,
        SyncEntityDeletion,
        SyncEntityData,
        AskForBackupEntityAuthority,
        CustomPacket,
    }
}
