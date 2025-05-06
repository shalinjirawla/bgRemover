using CommunityToolkit.Maui.Views;

namespace Plancton.Maui;

public partial class ColorPickerPopup : Popup
{
    public Color SelectedColor { get; private set; }

    public ColorPickerPopup()
    {
        InitializeComponent();
    }

    private void OnColorSelected(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            SelectedColor = button.BackgroundColor;
            Close(SelectedColor);
        }
    }
}