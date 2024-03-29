using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Havok;
using NLog;
using Sandbox;
using Sandbox.Engine.Networking;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.Gui;
using Torch.API;
using Torch.API.Managers;
using Torch.Collections;
using Torch.Managers;
using Torch.Mod;
using Torch.Server.ViewModels;
using Torch.Utils;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ObjectBuilder;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Private;
using VRage.Plugins;

namespace Torch.Server.Managers
{
    public class InstanceManager : Manager, IInstanceManager
    {
        private const string CONFIG_NAME = "SpaceEngineers-Dedicated.cfg";
        
        private Action<ConfigDedicatedViewModel> _instanceLoaded;

        /// <summary>
        /// Gets or sets the instance loaded event.
        /// </summary>
        /// <remarks>
        /// Called when the instance is loaded and immediately if subscribed after the instance is loaded.
        /// </remarks>
        public event Action<ConfigDedicatedViewModel> InstanceLoaded
        {
            add
            {
                var action = _instanceLoaded;
                Action<ConfigDedicatedViewModel> action2;
                do
                {
                    action2 = action;
                    var action3 = (Action<ConfigDedicatedViewModel>)Delegate.Combine(action2, value);
                    action = Interlocked.CompareExchange(ref _instanceLoaded, action3, action2);
                }
                while (action != action2);

                if (DedicatedConfig is not null)
                    value(DedicatedConfig);
            }
            remove
            {
                var action = _instanceLoaded;
                Action<ConfigDedicatedViewModel> action2;
                do
                {
                    action2 = action;
                    var action3 = (Action<ConfigDedicatedViewModel>)Delegate.Remove(action2, value);
                    action = Interlocked.CompareExchange(ref _instanceLoaded, action3, action2);
                }
                while (action != action2);
            }
        }

        public ConfigDedicatedViewModel DedicatedConfig { get; set; }
        private static readonly Logger Log = LogManager.GetLogger(nameof(InstanceManager));
        [Dependency]
        private FilesystemManager _filesystemManager;

        private new ITorchServer Torch { get; }

        public InstanceManager(ITorchServer torchInstance) : base(torchInstance)
        {
            Torch = torchInstance;
        }

        public IWorld SelectedWorld => DedicatedConfig.SelectedWorld;

        public void LoadInstance(string path, bool validate = true)
        {
            Log.Info($"Loading instance {path}");

            if (validate)
                ValidateInstance(path);

            MyFileSystem.Reset();
            MyFileSystem.Init("Content", path);
            //Initializes saves path. Why this isn't in Init() we may never know.
            MyFileSystem.InitUserSpecific(null);

            // why?....
            // var configPath = Path.Combine(path, CONFIG_NAME);
            // if (!File.Exists(configPath))
            // {
            //     Log.Error($"Failed to load dedicated config at {path}");
            //     return;
            // }

            
            // var config = new MyConfigDedicated<MyObjectBuilder_SessionSettings>(configPath);
            // config.Load(configPath);

            DedicatedConfig = new ConfigDedicatedViewModel((MyConfigDedicated<MyObjectBuilder_SessionSettings>) MySandboxGame.ConfigDedicated);

            var worldFolders = Directory.EnumerateDirectories(Path.Combine(Torch.InstancePath, "Saves"));

            foreach (var f in worldFolders)
            {
                try
                {
                    if (!string.IsNullOrEmpty(f) && File.Exists(Path.Combine(f, "Sandbox.sbc")))
                    {
                        var worldViewModel = new WorldViewModel(f, DedicatedConfig.LoadWorld == f);
                        DedicatedConfig.Worlds.Add(worldViewModel);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load world at path: " + f);
                }
            }

            if (DedicatedConfig.Worlds.Count == 0)
            {
                Log.Warn($"No worlds found in the current instance {path}.");
                return;
            }

            SelectWorld(DedicatedConfig.LoadWorld ?? DedicatedConfig.Worlds.First().WorldPath, false);

            _instanceLoaded?.Invoke(DedicatedConfig);
        }

        public void SelectCreatedWorld(string worldPath)
        {
            WorldViewModel worldViewModel;
            try
            {
                worldViewModel = new(worldPath);
                DedicatedConfig.Worlds.Add(worldViewModel);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load world at path: " + worldPath);
                return;
            }
            SelectWorld(worldViewModel, false);
        }

        public void SelectWorld(string worldPath, bool modsOnly = true)
        {
            DedicatedConfig.LoadWorld = worldPath;
            
            var worldInfo = DedicatedConfig.Worlds.FirstOrDefault(x => x.WorldPath == worldPath);
            try
            {
                if (worldInfo?.Checkpoint == null)
                {
                    worldInfo = new WorldViewModel(worldPath);
                    DedicatedConfig.Worlds.Add(worldInfo);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load world at path: " + worldPath);
                DedicatedConfig.LoadWorld = null;
                return;
            }

            DedicatedConfig.SelectedWorld = worldInfo;
            if (DedicatedConfig.SelectedWorld?.Checkpoint != null)
            {
                DedicatedConfig.Mods.Clear();
                //remove the Torch mod to avoid running multiple copies of it
                DedicatedConfig.SelectedWorld.WorldConfiguration.Mods.RemoveAll(m => m.PublishedFileId == ModCommunication.MOD_ID);
                foreach (var m in DedicatedConfig.SelectedWorld.WorldConfiguration.Mods)
                    DedicatedConfig.Mods.Add(new ModItemInfo(m));
                Task.Run(() => DedicatedConfig.UpdateAllModInfosAsync());
            }
        }

        public void SelectWorld(WorldViewModel world, bool modsOnly = true)
        {
            DedicatedConfig.LoadWorld = world.WorldPath;
            DedicatedConfig.SelectedWorld = world;
            if (DedicatedConfig.SelectedWorld?.Checkpoint != null)
            {
                DedicatedConfig.Mods.Clear();
                //remove the Torch mod to avoid running multiple copies of it
                DedicatedConfig.SelectedWorld.WorldConfiguration.Mods.RemoveAll(m => m.PublishedFileId == ModCommunication.MOD_ID);
                foreach (var m in DedicatedConfig.SelectedWorld.WorldConfiguration.Mods)
                    DedicatedConfig.Mods.Add(new ModItemInfo(m));
                Task.Run(() => DedicatedConfig.UpdateAllModInfosAsync());
            }
        }

        public void ImportSelectedWorldConfig()
        {
            ImportWorldConfig(DedicatedConfig.SelectedWorld, false);
        }

        private void ImportWorldConfig(WorldViewModel world, bool modsOnly = true)
        {
            var mods = new MtObservableList<ModItemInfo>();
            foreach (var mod in world.WorldConfiguration.Mods)
                mods.Add(new ModItemInfo(mod));
            DedicatedConfig.Mods = mods;


            Log.Debug("Loaded mod list from world");

            if (!modsOnly)
                DedicatedConfig.SessionSettings = world.WorldConfiguration.Settings;
        }

        private void ImportWorldConfig(bool modsOnly = true)
        {
            if (string.IsNullOrEmpty(DedicatedConfig.LoadWorld))
                return;

            var sandboxPath = Path.Combine(DedicatedConfig.LoadWorld, "Sandbox.sbc");

            if (!File.Exists(sandboxPath))
                return;

            try
            {
                MyObjectBuilderSerializer.DeserializeXML(sandboxPath, out MyObjectBuilder_Checkpoint checkpoint, out ulong sizeInBytes);
                if (checkpoint == null)
                {
                    Log.Error($"Failed to load {DedicatedConfig.LoadWorld}, checkpoint null ({sizeInBytes} bytes, instance {Torch.Config.InstancePath})");
                    return;
                }

                var mods = new MtObservableList<ModItemInfo>();
                foreach (var mod in checkpoint.Mods)
                    mods.Add(new ModItemInfo(mod));
                DedicatedConfig.Mods = mods;

                Log.Debug("Loaded mod list from world");

                if (!modsOnly)
                    DedicatedConfig.SessionSettings = new SessionSettingsViewModel(checkpoint.Settings);
            }
            catch (Exception e)
            {
                Log.Error($"Error loading mod list from world, verify that your mod list is accurate. '{DedicatedConfig.LoadWorld}'.");
                Log.Error(e);
            }
        }

        public void SaveConfig()
        {
            if (!((TorchServer)Torch).HasRun)
            {
                DedicatedConfig.Save(Path.Combine(Torch.InstancePath, CONFIG_NAME));
                Log.Info("Saved dedicated config.");
            }

            try
            {
                var world = DedicatedConfig.SelectedWorld;

                world.Checkpoint.SessionName = string.IsNullOrEmpty(world.Checkpoint.SessionName)
                    ? Path.GetDirectoryName(DedicatedConfig.LoadWorld)
                    : world.Checkpoint.SessionName;
                world.WorldConfiguration.Settings = DedicatedConfig.SessionSettings;
                world.WorldConfiguration.Mods.Clear();

                foreach (var mod in DedicatedConfig.Mods)
                {
                    var savedMod = ModItemUtils.Create(mod.PublishedFileId);
                    savedMod.IsDependency = mod.IsDependency;
                    savedMod.Name = mod.Name;
                    savedMod.FriendlyName = mod.FriendlyName;
                    
                    world.WorldConfiguration.Mods.Add(savedMod);
                }
                Task.Run(() => DedicatedConfig.UpdateAllModInfosAsync());

                world.SaveSandbox();

                Log.Info("Saved world config.");
            }
            catch (Exception e)
            {
                Log.Error("Failed to write sandbox config");
                Log.Error(e);
            }
        }

        /// <summary>
        /// Ensures that the given path is a valid server instance.
        /// </summary>
        private void ValidateInstance(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "Saves"));
            // Directory.CreateDirectory(Path.Combine(path, "Mods"));
            var configPath = Path.Combine(path, CONFIG_NAME);
            if (File.Exists(configPath))
                return;

            var config = new MyConfigDedicated<MyObjectBuilder_SessionSettings>(configPath);
            config.Save(configPath);
        }
    }

    public class WorldViewModel : ViewModel, IWorld
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public string WorldPath { get; }
        public MyObjectBuilder_SessionSettings KeenSessionSettings => WorldConfiguration.Settings;
        public MyObjectBuilder_Checkpoint KeenCheckpoint => Checkpoint;
        public long WorldSizeKB { get; }
        private string _checkpointPath;
        private string _worldConfigPath;
        private CheckpointViewModel _checkpoint;

        public CheckpointViewModel Checkpoint
        {
            get
            {
                if (_checkpoint is null) LoadSandbox();
                return _checkpoint;
            }
            private set => _checkpoint = value;
        }

        public WorldConfigurationViewModel WorldConfiguration { get; private set; }

        public WorldViewModel(string worldPath, bool loadFiles = true)
        {
            try
            {
                WorldPath = worldPath;
                WorldSizeKB = new DirectoryInfo(worldPath).GetFiles().Sum(x => x.Length) / 1024;
                _checkpointPath = Path.Combine(WorldPath, "Sandbox.sbc");
                _worldConfigPath = Path.Combine(WorldPath, "Sandbox_config.sbc");
                if (loadFiles)
                    LoadSandbox();
            }
            catch (ArgumentException ex)
            {
                Log.Error($"World view model failed to load the path: {worldPath} Please ensure this is a valid path.");
                throw; //rethrow to be handled further up the stack
            }
        }

        public void SaveSandbox()
        {
            using (var f = File.Open(_checkpointPath, FileMode.Create))
                MyObjectBuilderSerializerKeen.SerializeXML(f, Checkpoint);

            using (var f = File.Open(_worldConfigPath, FileMode.Create))
                MyObjectBuilderSerializerKeen.SerializeXML(f, WorldConfiguration);
        }

        public void LoadSandbox()
        {
            if (!MyObjectBuilderSerializer.DeserializeXML(_checkpointPath, out MyObjectBuilder_Checkpoint checkpoint))
                throw new SerializationException("Error reading checkpoint, see keen log for details");
            Checkpoint = new CheckpointViewModel(checkpoint);
            
            // migrate old saves
            if (File.Exists(_worldConfigPath))
            {
                if (!MyObjectBuilderSerializer.DeserializeXML(_worldConfigPath, out MyObjectBuilder_WorldConfiguration worldConfig))
                    throw new SerializationException("Error reading settings, see keen log for details");
                WorldConfiguration = new WorldConfigurationViewModel(worldConfig);
            }
            else
            {
                WorldConfiguration = new WorldConfigurationViewModel(new MyObjectBuilder_WorldConfiguration
                {
                    Mods = checkpoint.Mods,
                    Settings = checkpoint.Settings
                });

                checkpoint.Mods = null;
                checkpoint.Settings = null;
            }
        }
    }
}
