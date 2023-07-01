using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NLog;
using Sandbox;
using Sandbox.Game;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Torch.API;
using Torch.API.Managers;
using Torch.API.ModAPI;
using Torch.API.Session;
using Torch.Commands;
using Torch.Event;
using Torch.Managers;
using Torch.Managers.ChatManager;
using Torch.Managers.PatchManager;
using Torch.Packages;
using Torch.Patches;
using Torch.Utils;
using Torch.Session;
using VRage.Plugins;
using VRage.Utils;

namespace Torch
{
    /// <summary>
    /// Base class for code shared between the Torch client and server.
    /// </summary>
    public abstract class TorchBase : ViewModel, ITorchBase, IPlugin
    {
        static TorchBase()
        {
            TorchLauncher.RegisterAssembly(typeof(ITorchBase).Assembly);
            TorchLauncher.RegisterAssembly(typeof(TorchBase).Assembly);
            TorchLauncher.RegisterAssembly(Assembly.GetEntryAssembly());
            
            PatchManager.AddPatchShim(typeof(GameStatePatchShim));
            PatchManager.AddPatchShim(typeof(GameAnalyticsPatch));
            PatchManager.AddPatchShim(typeof(KeenLogPatch));
            PatchManager.AddPatchShim(typeof(XmlRootWriterPatch));
            PatchManager.CommitInternal();
            
            //exceptions in English, please
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
        }

        /// <summary>
        /// Hack because *keen*.
        /// Use only if necessary, prefer dependency injection.
        /// </summary>
        [Obsolete("This is a hack, don't use it.")]
        public static ITorchBase Instance { get; private set; }

        /// <inheritdoc />
        public ITorchConfig Config { get; protected set; }

        public abstract IConfiguration Configuration { get; }

        /// <inheritdoc />
        public SemanticVersioning.Version TorchVersion { get; }
        public string InstancePath { get; protected init;}
        public string InstanceName { get; protected init; }

        /// <inheritdoc />
        public Version GameVersion { get; private set; }

        /// <inheritdoc />
        public string[] RunArgs { get; set; }

        /// <inheritdoc />
        [Obsolete("Use GetManager<T>() or the [Dependency] attribute.")]
        public IPluginManager Plugins { get; protected set; }

        /// <inheritdoc />
        public ITorchSession CurrentSession => Managers?.GetManager<ITorchSessionManager>()?.CurrentSession;

        /// <summary>
        /// Common log for the Torch instance.
        /// </summary>
        protected static Logger Log { get; } = LogManager.GetLogger("Torch");

        /// <inheritdoc/>
        public IDependencyManager Managers { get; }

        private bool _init;

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="InvalidOperationException">Thrown if a TorchBase instance already exists.</exception>
        protected TorchBase(ITorchConfig config)
        {
#pragma warning disable CS0618
            if (Instance != null)
                throw new InvalidOperationException("A TorchBase instance already exists.");

            Instance = this;
#pragma warning restore CS0618
            Config = config;

            var assemblyVersion = GetType().Assembly.GetName().Version ?? new Version();
            TorchVersion = new(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);

            RunArgs = Array.Empty<string>();

            Managers = new DependencyManager();

#pragma warning disable CS0618
            Plugins = new PluginManager(this);
#pragma warning restore CS0618

            var sessionManager = new TorchSessionManager(this);
            sessionManager.AddFactory(_ => Sync.IsServer ? new ChatManagerServer(this) : new ChatManagerClient(this));
            sessionManager.AddFactory(_ => Sync.IsServer ? new CommandManager(this) : null);
            sessionManager.AddFactory(_ => new EntityManager(this));

            Managers.AddManager(sessionManager);
            Managers.AddManager(new PatchManager(this));
            Managers.AddManager(new FilesystemManager(this));
            Managers.AddManager(new UpdateManager(this));
            Managers.AddManager(new EventManager(this));
#pragma warning disable CS0618
            Managers.AddManager(Plugins);
            Managers.AddManager(new PackageManager(this, (PluginManager)Plugins));
#pragma warning restore CS0618
            Managers.AddManager(new ScriptCompilationManager(this));
            TorchAPI.Instance = this;

            GameStateChanged += (game, state) =>
            {
                if (state == TorchGameState.Created) 
                    PatchManager.CommitInternal();
            };

            var harmonyLog = LogManager.GetLogger("HarmonyX");
            HarmonyLib.Tools.Logger.ChannelFilter = HarmonyLib.Tools.Logger.LogChannel.Debug;
            HarmonyLib.Tools.Logger.MessageReceived += (_, args) => harmonyLog.Log(args.LogChannel switch
            {
                HarmonyLib.Tools.Logger.LogChannel.None => LogLevel.Off,
                HarmonyLib.Tools.Logger.LogChannel.Info => LogLevel.Info,
                HarmonyLib.Tools.Logger.LogChannel.IL => LogLevel.Trace,
                HarmonyLib.Tools.Logger.LogChannel.Warn => LogLevel.Warn,
                HarmonyLib.Tools.Logger.LogChannel.Error => LogLevel.Error,
                HarmonyLib.Tools.Logger.LogChannel.Debug => LogLevel.Debug,
                HarmonyLib.Tools.Logger.LogChannel.All => LogLevel.Debug,
                _ => throw new ArgumentOutOfRangeException()
            }, args.Message);
        }

        [Obsolete("Prefer using Managers.GetManager for global managers")]
        public T GetManager<T>() where T : class, IManager
        {
            return Managers.GetManager<T>();
        }

        [Obsolete("Prefer using Managers.AddManager for global managers")]
        public bool AddManager<T>(T manager) where T : class, IManager
        {
            return Managers.AddManager(manager);
        }

        #region Game Actions

        /// <summary>
        /// Invokes an action on the game thread.
        /// </summary>
        /// <param name="action"></param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke(Action action, [CallerMemberName] string caller = "")
        {
            MySandboxGame.Static.Invoke(action, caller);
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InvokeBlocking(Action action, int timeoutMs = -1, [CallerMemberName] string caller = "")
        {
            if (Thread.CurrentThread == MySandboxGame.Static.UpdateThread)
            {
                Debug.Assert(false, $"{nameof(InvokeBlocking)} should not be called on the game thread.");
                // ReSharper disable once HeuristicUnreachableCode
                action.Invoke();
                return;
            }

            // ReSharper disable once ExplicitCallerInfoArgument
            Task task = InvokeAsync(action, caller);
            if (!task.Wait(timeoutMs))
                throw new TimeoutException("The game action timed out");
            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task<T> InvokeAsync<T>(Func<T> action, [CallerMemberName] string caller = "")
        {
            var ctx = new TaskCompletionSource<T>();
            MySandboxGame.Static.Invoke(() =>
            {
                try
                {
                    ctx.SetResult(action.Invoke());
                }
                catch (Exception e)
                {
                    ctx.SetException(e);
                }
                finally
                {
                    Debug.Assert(ctx.Task.IsCompleted);
                }
            }, caller);
            return ctx.Task;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task InvokeAsync(Action action, [CallerMemberName] string caller = "")
        {
            var ctx = new TaskCompletionSource<bool>();
            MySandboxGame.Static.Invoke(() =>
            {
                try
                {
                    action.Invoke();
                    ctx.SetResult(true);
                }
                catch (Exception e)
                {
                    ctx.SetException(e);
                }
                finally
                {
                    Debug.Assert(ctx.Task.IsCompleted);
                }
            }, caller);
            return ctx.Task;
        }

#endregion

#region Torch Init/Destroy

        protected abstract uint SteamAppId { get; }
        protected abstract string SteamAppName { get; }

        /// <inheritdoc />
        public virtual void Init()
        {
            Debug.Assert(!_init, "Torch instance is already initialized.");
            VRageGame.SetupVersionInfo();

            Debug.Assert(MyPerGameSettings.BasicGameInfo.GameVersion != null, "MyPerGameSettings.BasicGameInfo.GameVersion != null");
            GameVersion = new MyVersion(MyPerGameSettings.BasicGameInfo.GameVersion.Value);

            try
            {
                Console.Title = $"{InstanceName} - Torch {TorchVersion}, SE {GameVersion}";
            }
            catch
            {
                // Running without a console
            }

#if DEBUG
            Log.Info("DEBUG");
#else
            Log.Info("RELEASE");
#endif
            Log.Info($"Torch Version: {TorchVersion}");
            Log.Info($"Game Version: {GameVersion}");
            Log.Info($"Executing assembly: {Assembly.GetEntryAssembly()?.FullName}");
            Log.Info($"Executing directory: {AppDomain.CurrentDomain.BaseDirectory}");

            Managers.GetManager<PluginManager>().LoadPlugins();

            var semaphore = new SemaphoreSlim(0, 1);
            Managers.GetManager<PackageManager>().LoadAsync(semaphore);
            semaphore.Wait();

            Game = new VRageGame(this, TweakGameSettings, SteamAppName, SteamAppId, InstancePath, RunArgs);
            if (!Game.WaitFor(VRageGame.GameState.Stopped))
                Log.Warn("Failed to wait for game to be initialized");
            Managers.Attach();
            _init = true;

            if (GameState is >= TorchGameState.Created and < TorchGameState.Unloading)
                // safe to commit here; all important static ctors have run
                PatchManager.CommitInternal();
        }

        /// <summary>
        /// Dispose callback for VRage plugin.  Do not use.
        /// </summary>
        [Obsolete("Do not use; only there for VRage capability")]
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public virtual void Destroy()
        {
            Managers.Detach();
            Game.SignalDestroy();
            if (!Game.WaitFor(VRageGame.GameState.Destroyed))
                Log.Warn("Failed to wait for the game to be destroyed");
            Game = null;
        }

#endregion

        protected VRageGame Game { get; private set; }

        /// <summary>
        /// Called after the basic game information is filled, but before the game is created.
        /// </summary>
        protected virtual void TweakGameSettings()
        {
        }


        private int _inProgressSaves = 0;
        /// <inheritdoc/>
        public virtual Task<GameSaveResult> Save(int timeoutMs = -1, bool exclusive = false)
        {
            if (exclusive)
            {
                if (MyAsyncSaving.InProgress || _inProgressSaves > 0)
                {
                    Log.Error("Failed to save game, game is already saving");
                    return null;
                }
            }
            
            Interlocked.Increment(ref _inProgressSaves);
            return TorchAsyncSaving.Save(this, timeoutMs).ContinueWith((task, torchO) =>
            {
                var torch = (TorchBase) torchO;
                Interlocked.Decrement(ref torch._inProgressSaves);
                if (task.IsFaulted)
                {
                    Log.Error(task.Exception, "Failed to save game");
                    return GameSaveResult.UnknownError;
                }
                if (task.Result != GameSaveResult.Success)
                    Log.Error($"Failed to save game: {task.Result}");
                else
                    Log.Info("Saved game");
                return task.Result;
            }, this, TaskContinuationOptions.RunContinuationsAsynchronously);
        }

        /// <inheritdoc/> 
        public virtual void Start()
        {
            Game.SignalStart();
            if (!Game.WaitFor(VRageGame.GameState.Running))
                Log.Warn("Failed to wait for the game to be started");
            Invoke(() => Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US"));
        }

        /// <inheritdoc />
        public virtual void Stop()
        {
            LogManager.Flush();
            Game.SignalStop();
            if (!Game.WaitFor(VRageGame.GameState.Stopped))
                Log.Warn("Failed to wait for the game to be stopped");
        }

        /// <inheritdoc />
        public abstract void Restart(bool save = true);

        /// <inheritdoc />
        public virtual void Init(object gameInstance)
        {
        }

        /// <inheritdoc />
        public virtual void Update()
        {
            Managers.GetManager<IPluginManager>().UpdatePlugins();
        }


        private TorchGameState _gameState = TorchGameState.Unloaded;

        /// <inheritdoc/>
        public TorchGameState GameState
        {
            get => _gameState;
            internal set
            {
                _gameState = value;
                GameStateChanged?.Invoke(MySandboxGame.Static, _gameState);
            }
        }

        /// <inheritdoc/>
        public event TorchGameStateChangedDel GameStateChanged;
    }
}