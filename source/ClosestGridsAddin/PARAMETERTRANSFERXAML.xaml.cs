using System.Windows;

namespace ClosestGridsAddinVANILLA
{
    /// <summary>
    /// Lógica de interacción para PARAMETERTRANSFERXAML.xaml
    /// </summary>
    public partial class PARAMETERTRANSFERXAML : Window
    {
        public PARAMETERTRANSFERXAML()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
