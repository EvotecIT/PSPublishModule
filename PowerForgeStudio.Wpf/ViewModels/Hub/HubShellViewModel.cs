namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class HubShellViewModel : ViewModelBase
{
    private readonly ShellViewModel _releaseViewModel;
    private readonly HubViewModel _hubViewModel;
    private ViewModelBase _activeContent;
    private bool _isHubMode = true;

    public HubShellViewModel(ShellViewModel releaseViewModel, HubViewModel hubViewModel)
    {
        _releaseViewModel = releaseViewModel ?? throw new ArgumentNullException(nameof(releaseViewModel));
        _hubViewModel = hubViewModel ?? throw new ArgumentNullException(nameof(hubViewModel));
        _activeContent = _hubViewModel;

        SwitchToHubCommand = new DelegateCommand<object?>(_ =>
        {
            IsHubMode = true;
            ActiveContent = _hubViewModel;
        });

        SwitchToReleaseCommand = new DelegateCommand<object?>(_ =>
        {
            IsHubMode = false;
            ActiveContent = _releaseViewModel;
        });
    }

    public ViewModelBase ActiveContent
    {
        get => _activeContent;
        private set => SetProperty(ref _activeContent, value);
    }

    public bool IsHubMode
    {
        get => _isHubMode;
        private set
        {
            if (SetProperty(ref _isHubMode, value))
            {
                RaisePropertyChanged(nameof(IsReleaseMode));
            }
        }
    }

    public bool IsReleaseMode => !_isHubMode;

    public ShellViewModel ReleaseViewModel => _releaseViewModel;

    public HubViewModel HubViewModel => _hubViewModel;

    public DelegateCommand<object?> SwitchToHubCommand { get; }

    public DelegateCommand<object?> SwitchToReleaseCommand { get; }

    public async Task InitializeAsync()
    {
        await _hubViewModel.InitializeAsync().ConfigureAwait(true);
    }
}
