using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using NLog;

namespace Torch
{
    /// <summary>
    /// Simple class that manages saving <see cref="Persistent{T}.Data"/> to disk using XML serialization.
    /// Can automatically save on changes by implementing <see cref="INotifyPropertyChanged"/> in the data class.
    /// </summary>
    /// <typeparam name="T">Data class type</typeparam>
    public sealed class Persistent<T> : IDisposable where T : new()
    {
        private static readonly XmlSerializer Serializer = new(typeof(T));
        private static readonly XmlSerializerNamespaces Namespaces = new(new[] {new XmlQualifiedName("", "")});
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public string Path { get; set; }
        private T _data;
        public T Data
        {
            get => _data;
            private init
            {
                if (_data is INotifyPropertyChanged npc1)
                    npc1.PropertyChanged -= OnPropertyChanged;
                _data = value;
                if (_data is INotifyPropertyChanged npc2)
                    npc2.PropertyChanged += OnPropertyChanged;
            }
        }

        ~Persistent()
        {
            DisposeInternal();
        }

        public Persistent(string path, T data = default(T))
        {
            Path = path;
            Data = data;
        }
        
        private Timer _saveConfigTimer;

        private void SaveAsync()
        {
            _saveConfigTimer ??= new(_ => Save());

            _saveConfigTimer.Change(1000, -1);
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveAsync();
        }

        public void Save(string path = null)
        {
            if (path == null)
                path = Path;

            using var f = File.Create(path);
            using var writer = new XmlTextWriter(f, Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Namespaces = false
            };
            Serializer.Serialize(writer, Data, Namespaces);
        }

        public static Persistent<T> Load(string path, bool saveIfNew = true)
        {
            Persistent<T> config = null;

            if (File.Exists(path))
            {
                try
                {
                    using (var f = File.OpenText(path))
                    {
                        config = new Persistent<T>(path, (T)Serializer.Deserialize(f));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                    config = null;
                }
            }
            if (config == null)
                config = new Persistent<T>(path, new T());
            if (!File.Exists(path) && saveIfNew)
                config.Save();

            return config;
        }

        public void Dispose()
        {
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        private void DisposeInternal()
        {
            try
            {
                if (Data is INotifyPropertyChanged npc)
                    npc.PropertyChanged -= OnPropertyChanged;
                _saveConfigTimer?.Dispose();
                Save();
            }
            catch
            {
                // ignored
            }
        }
    }
}
