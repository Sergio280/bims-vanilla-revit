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
    /// Lógica de interacción para ACEROCOLUMNASXAML.xaml
    /// </summary>
    public partial class ACEROCOLUMNASXAML : Window
    {
        public ACEROCOLUMNASXAML(List<string> REBARTYPES)
        {
            InitializeComponent();
            this.diametro.ItemsSource = REBARTYPES;
            this.diametro.SelectedItem = REBARTYPES[0];
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
