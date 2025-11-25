using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ClosestGridsAddinVANILLA.ACERO
{
    /// <summary>
    /// Lógica de interacción para ACEROVIGASXAML.xaml
    /// </summary>
    public partial class ACEROVIGASXAML : Window
    {
        public ACEROVIGASXAML(List<string> REBARTYPES,bool estribosActivado, bool longitudActivada)
        {
            InitializeComponent();
            this.barras.ItemsSource = REBARTYPES;
            this.barras.SelectedItem = REBARTYPES[0];
            this.estribos.ItemsSource = REBARTYPES;
            this.estribos.SelectedItem = REBARTYPES[0];
            this.activarEstribos.IsChecked = estribosActivado;
            this.activarLong.IsChecked= longitudActivada;

        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
        private void Checked(object sender, RoutedEventArgs e)
        {

        }
        private void HandleUnchecked(object sender, RoutedEventArgs e)
        {

        }
    }
}
