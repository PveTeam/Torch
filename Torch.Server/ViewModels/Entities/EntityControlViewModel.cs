﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Torch.Server.ViewModels.Entities
{
    public class EntityControlViewModel : ViewModel
    {
        internal const string SignalPropertyInvalidateControl =
            "InvalidateControl-4124a476-704f-4762-8b5e-336a18e2f7e5";

        internal void InvalidateControl()
        {
            // ReSharper disable once ExplicitCallerInfoArgument
            OnPropertyChanged(SignalPropertyInvalidateControl);
        }

        /// <summary>
        /// Should this element be forced into the <see cref="Visibility.Collapsed"/>
        /// </summary>
        public bool Hide { get; set; }
    }
}
