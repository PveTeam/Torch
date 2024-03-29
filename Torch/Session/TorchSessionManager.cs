﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Sandbox.Engine.Networking;
using Sandbox.Game.World;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Session;
using Torch.Managers;
using Torch.Mod;
using Torch.Session;
using Torch.Utils;
using VRage.Game;

namespace Torch.Session
{
    /// <summary>
    /// Manages the creation and destruction of <see cref="TorchSession"/> instances for each <see cref="MySession"/> created by Space Engineers.
    /// </summary>
    public class TorchSessionManager : Manager, ITorchSessionManager
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private TorchSession _currentSession;

        private readonly Dictionary<ulong, MyObjectBuilder_Checkpoint.ModItem> _overrideMods;

        [Dependency]
        private IInstanceManager _instanceManager = null!;

        public event Action<CollectionChangeEventArgs> OverrideModsChanged;

        /// <summary>
        /// List of mods that will be injected into client world downloads.
        /// </summary>
        public IReadOnlyCollection<MyObjectBuilder_Checkpoint.ModItem> OverrideMods => _overrideMods.Values;

        /// <inheritdoc />
        public event TorchSessionStateChangedDel SessionStateChanged;

        /// <inheritdoc/>
        public ITorchSession CurrentSession => _currentSession;

        private readonly HashSet<SessionManagerFactoryDel> _factories = new HashSet<SessionManagerFactoryDel>();

        public TorchSessionManager(ITorchBase torchInstance) : base(torchInstance)
        {
            _overrideMods = new Dictionary<ulong, MyObjectBuilder_Checkpoint.ModItem>();
        }

        /// <inheritdoc/>
        public bool AddFactory(SessionManagerFactoryDel factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory), "Factory must be non-null");
            return _factories.Add(factory);
        }

        /// <inheritdoc/>
        public bool RemoveFactory(SessionManagerFactoryDel factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory), "Factory must be non-null");
            return _factories.Remove(factory);
        }

        /// <inheritdoc/>
        public bool AddOverrideMod(ulong modId)
        {
            if (_overrideMods.ContainsKey(modId))
                return false;
#pragma warning disable CS0618
            var item = ModItemUtils.Create(modId, TorchBase.Instance.Config.UgcServiceType.ToString());
#pragma warning restore CS0618
            _overrideMods.Add(modId, item);

            OverrideModsChanged?.Invoke(new CollectionChangeEventArgs(CollectionChangeAction.Add, item));
            return true;
        }

        /// <inheritdoc/>
        public bool RemoveOverrideMod(ulong modId)
        {
            if(_overrideMods.TryGetValue(modId, out var item))
                OverrideModsChanged?.Invoke(new CollectionChangeEventArgs(CollectionChangeAction.Remove, item));

            return _overrideMods.Remove(modId);
        }

        #region Session events

        private void SetState(TorchSessionState state)
        {
            if (_currentSession == null)
                return;
            _currentSession.State = state;
            SessionStateChanged?.Invoke(_currentSession, _currentSession.State);
        }

        private void SessionLoading()
        {
            try
            {
                if (_instanceManager.SelectedWorld is null)
                    throw new InvalidOperationException("No valid worlds selected! Please select world first.");
                
                if (_currentSession != null)
                {
                    _log.Warn($"Override old torch session {_currentSession.KeenSession.Name}");
                    _currentSession.Detach();
                }

                _log.Info($"Starting new torch session for {_instanceManager.SelectedWorld.KeenCheckpoint.SessionName}");

                _currentSession = new TorchSession(Torch, MySession.Static, _instanceManager.SelectedWorld);
                SetState(TorchSessionState.Loading);
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw;
            }
        }

        private void SessionLoaded()
        {
            try
            {
                if (_currentSession is null)
                    throw new InvalidOperationException("Session loaded event occurred when we don't have a session.");
                
                foreach (SessionManagerFactoryDel factory in _factories)
                {
                    IManager manager = factory(CurrentSession);
                    if (manager != null)
                        CurrentSession.Managers.AddManager(manager);
                }
                (CurrentSession as TorchSession)?.Attach();
                _log.Info($"Loaded torch session for {CurrentSession.World.KeenCheckpoint.SessionName}");
                SetState(TorchSessionState.Loaded);
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw;
            }
        }

        private void SessionUnloading()
        {
            try
            {
                if (_currentSession is null)
                    throw new InvalidOperationException("Session loaded event occurred when we don't have a session.");
                
                _log.Info($"Unloading torch session for {_currentSession.World.KeenCheckpoint.SessionName}");
                SetState(TorchSessionState.Unloading);
                _currentSession.Detach();
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw;
            }
        }

        private void SessionUnloaded()
        {
            try
            {
                if (_currentSession is null)
                    throw new InvalidOperationException("Session loaded event occurred when we don't have a session.");
                
                _log.Info($"Unloaded torch session for {_currentSession.World.KeenCheckpoint.SessionName}");
                SetState(TorchSessionState.Unloaded);
                _currentSession = null;
            }
            catch (Exception e)
            {
                _log.Error(e);
                throw;
            }
        }
        #endregion

        /// <inheritdoc/>
        public override void Attach()
        {
            MySession.OnLoading += SessionLoading;
            MySession.AfterLoading += SessionLoaded;
            MySession.OnUnloading += SessionUnloading;
            MySession.OnUnloaded += SessionUnloaded;
            if (Torch.Config.UgcServiceType == UGCServiceType.Steam)
                _overrideMods.Add(ModCommunication.MOD_ID, ModItemUtils.Create(ModCommunication.MOD_ID));
        }


        /// <inheritdoc/>
        public override void Detach()
        {
            MySession.OnLoading -= SessionLoading;
            MySession.AfterLoading -= SessionLoaded;
            MySession.OnUnloading -= SessionUnloading;
            MySession.OnUnloaded -= SessionUnloaded;

            if (_currentSession != null)
            {
                if (_currentSession.State == TorchSessionState.Loaded)
                    SessionUnloading();
                if (_currentSession.State == TorchSessionState.Unloading)
                    SessionUnloaded();
            }
        }
    }
}
