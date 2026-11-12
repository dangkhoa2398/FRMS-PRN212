using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class CustomerPageViewModel : ObservableObject
{
    public CustomerPageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }
    public ObservableCollection<string> Items { get; } = new();

    private string _message = "Loading...";
    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
}
