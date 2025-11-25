using Autodesk.Revit.DB.Structure;
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
    /// Lógica de interacción para ACEROLOSASYCIMIENTOSXAML.xaml
    /// </summary>
    public partial class ACEROLOSASYCIMIENTOSXAML : Window
    {
        public ACEROLOSASYCIMIENTOSXAML(List<string> REBARTYPES, List<string> REBARHOOKTYPES, bool inferiorActivado, bool superiorActivado, List<string> RECUBRIMIENTO)
        {
            InitializeComponent();
            this.diametroBarra.ItemsSource = REBARTYPES;
            this.tipodegancho.ItemsSource = REBARHOOKTYPES;
            this.recubrimiento.ItemsSource = RECUBRIMIENTO;
            this.activarganchoinferior.IsChecked = inferiorActivado;
            this.activarganchosuperior.IsChecked = superiorActivado; 
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
