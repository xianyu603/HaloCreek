using System.Windows.Input;

namespace HaloCreek.ViewModels.Tabs
{
    public interface IGitFileAction
    {
        string Label { get; }

        ICommand Command { get; }
    }
}
