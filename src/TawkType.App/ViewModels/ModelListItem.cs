using CommunityToolkit.Mvvm.ComponentModel;
using TawkType.Desktop.Services;

namespace TawkType.Desktop.ViewModels;

/// <summary>One tickable row in the Settings window's model list.</summary>
public sealed partial class ModelListItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ModelListItem(InstalledModel model)
    {
        Model = model;
    }

    public InstalledModel Model { get; }

    public string Label => Model.Label;

    public string Detail => Model.InUse
        ? $"{Model.SizeText} · in use"
        : Model.SizeText;
}
