using System;
using Sandbox.ModAPI;
using Torch.Mod.Messages;
using VRage.Game.Components;
using VRage.Game.ModAPI;
#if TORCH
using Torch.Utils;
using VRage.Library.Collections;
using System.Reflection;
#endif

namespace Torch.Mod
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ModCommunication : MySessionComponentBase
    {
        public const ulong MOD_ID = 2915950488;
        private const ushort CHANNEL = 7654;

        public override void BeforeStart()
        {
            base.BeforeStart();
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(CHANNEL, MessageHandler);
        }

        private void MessageHandler(ushort channel, byte[] data, ulong sender, bool fromServer)
        {
            if (!fromServer)
                return;

            var message = MyAPIGateway.Utilities.SerializeFromBinary<MessageBase>(data);
            message.SenderId = sender;
            
            if (MyAPIGateway.Multiplayer.IsServer) message.ProcessServer();
            else message.ProcessClient();
        }

#if TORCH
        [ReflectedMethodInfo(typeof(MyAPIUtilities), "VRage.Game.ModAPI.IMyUtilities.SerializeToBinary")]
        private static MethodInfo _serializeMethod = null!;

        private static readonly CacheList<IMyPlayer> Players = new();

        private static byte[] Serialize(MessageBase message)
        {
            return (byte[])_serializeMethod.MakeGenericMethod(message.GetType())
                                          .Invoke(MyAPIGateway.Utilities, new object[] { message });
        }
        
        public static void SendMessageTo(MessageBase message, ulong target)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Only server can send targeted messages");

            MyAPIGateway.Multiplayer.SendMessageTo(CHANNEL, Serialize(message), target);
        }

        public static void SendMessageToClients(MessageBase message)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Only server can send targeted messages");

            MyAPIGateway.Multiplayer.SendMessageToOthers(CHANNEL, Serialize(message));
        }

        public static void SendMessageExcept(MessageBase message, params ulong[] ignoredUsers)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception("Only server can send targeted messages");

            using var players = Players;
            MyAPIGateway.Multiplayer.Players.GetPlayers(players, player => !ignoredUsers.Contains(player.SteamUserId));

            var data = Serialize(message);
            foreach (var player in players)
            {
                MyAPIGateway.Multiplayer.SendMessageTo(CHANNEL, data, player.SteamUserId);
            }
        }

        public static void SendMessageToServer(MessageBase message)
        {
            throw new NotSupportedException();
        }
#endif
    }
}
