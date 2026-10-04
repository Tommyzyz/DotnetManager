using System.ComponentModel;
using DotnetManager.RazorLib.Features.Common;
using Microsoft.AspNetCore.Components;

namespace DotnetManager.RazorLib.Shared;

/// <summary>
/// Base class for pages that render a view model. The page re-renders in place when the view model changes or
/// when the UI language changes; it is never created, disposed or navigated to by the localization switch.
/// </summary>
public abstract class ViewModelPage<TViewModel> : ComponentBase, IDisposable
    where TViewModel : INotifyPropertyChanged
{
    [Inject]
    protected TViewModel ViewModel { get; set; } = default!;

    [Inject]
    protected LocalizationState Localization { get; set; } = default!;

    protected override void OnInitialized()
    {
        ViewModel.PropertyChanged += HandleViewModelChanged;
        Localization.Changed += HandleLanguageChanged;
    }

    void IDisposable.Dispose()
    {
        ViewModel.PropertyChanged -= HandleViewModelChanged;
        Localization.Changed -= HandleLanguageChanged;
        DisposeViewModel();
    }

    /// <summary>Override to release view model resources when the page is destroyed.</summary>
    protected virtual void DisposeViewModel()
    {
    }

    private void HandleViewModelChanged(object? sender, PropertyChangedEventArgs e) =>
        _ = InvokeAsync(StateHasChanged);

    private void HandleLanguageChanged() => _ = InvokeAsync(StateHasChanged);
}
