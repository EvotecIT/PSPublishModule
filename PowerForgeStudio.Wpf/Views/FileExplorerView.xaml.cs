using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerForgeStudio.Wpf.ViewModels.Hub;

namespace PowerForgeStudio.Wpf.Views;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FileExplorerViewModel vm && e.NewValue is FileTreeNodeViewModel node)
        {
            vm.OnTreeSelectionChanged(node);
        }
    }

    private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is FileExplorerViewModel vm
            && sender is ListView listView
            && listView.SelectedItem is FileListItemViewModel item)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }
}
