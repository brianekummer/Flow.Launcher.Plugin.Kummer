using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Flow.Launcher.Plugin.Kummer
{
    /// <summary>
    /// Interaction logic for KummerSettings.xaml
    /// </summary>
    public partial class KummerSettings : UserControl
    {
        public Settings Settings { get; }

        public KummerSettings(Settings settings)
        {
            Settings = settings;
            InitializeComponent();
        }
    }
}
