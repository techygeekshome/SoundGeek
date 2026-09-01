using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SoundGeek.Views;

public partial class ModelView : UserControl
{
    public ModelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
