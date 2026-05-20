using System;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HaloCreek.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Icon = new WindowIcon(AssetLoader.Open(new Uri(AppIconResource)));
        }

#if PUBLISH_ICON
        private const string AppIconResource = "avares://HaloCreek/Assets/publish.ico";
#else
        private const string AppIconResource = "avares://HaloCreek/Assets/avalonia-logo.ico";
#endif
    }
}
