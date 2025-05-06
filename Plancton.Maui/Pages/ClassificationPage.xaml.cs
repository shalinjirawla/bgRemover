using Plancton.Maui.ViewModels;
using System.ComponentModel;

namespace Plancton.Maui.Pages;

public partial class ClassificationPage : ContentPage, INotifyPropertyChanged
{
    public ClassificationPage()
	{
		InitializeComponent();
        BindingContext = new ClassificationViewModel();
    }

    private void Entry_Completed(object sender, EventArgs e)
    {
        if (BindingContext is ClassificationViewModel vm && vm.AddChipCommand.CanExecute(null))
        {
            vm.AddChipCommand.Execute(null);
        }
    }
}