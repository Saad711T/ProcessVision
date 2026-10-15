using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace ProcessesVision.Infrastructure;
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
