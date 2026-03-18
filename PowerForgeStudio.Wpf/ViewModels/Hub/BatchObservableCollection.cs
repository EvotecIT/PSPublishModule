using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnCollectionChanged(e);
        }
    }
}
