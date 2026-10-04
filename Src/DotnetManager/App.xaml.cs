using System.Windows;
using static DotnetManager.Startup;

namespace DotnetManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        Init();
        InitializeComponent();
    }
}
