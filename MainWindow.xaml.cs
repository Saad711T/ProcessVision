using System.Windows;
using ProcessesVision.ViewModels;
namespace ProcessesVision;
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel = new MainViewModel();
        Closed += (_, _) => _viewModel.Dispose();
    }
}
