using System.Windows;

namespace Calculator
{
    /// <summary>
    /// SettingsWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = new CalculatorApp.ViewModel.SettingsViewModel();
        }
    }
}
