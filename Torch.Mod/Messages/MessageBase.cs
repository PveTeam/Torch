using ProtoBuf;

namespace Torch.Mod.Messages
{
    #region Includes
    [ProtoInclude(1, typeof(DialogMessage))]
    [ProtoInclude(2, typeof(NotificationMessage))]
    [ProtoInclude(3, typeof(VoxelResetMessage))]
    [ProtoInclude(4, typeof(JoinServerMessage))]
    #endregion

    [ProtoContract]
    public abstract class MessageBase
    {
        public ulong SenderId;

        public abstract void ProcessClient();
        public abstract void ProcessServer();
    }
}
