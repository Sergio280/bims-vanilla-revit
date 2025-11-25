using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace ClosestGridsAddinVANILLA.Views
{
    public partial class FormwBimsDialog : Window
    {
        public List<BuiltInCategory> CategoriasSeleccionadas { get; private set; }
        public bool UserAccepted { get; private set; }

        public FormwBimsDialog()
        {
            InitializeComponent();
            CategoriasSeleccionadas = new List<BuiltInCategory>();
            UserAccepted = false;

            // Habilitar botón de continuar cuando al menos una categoría esté seleccionada
            ChkColumnas.Checked += CheckBox_CheckedChanged;
            ChkColumnas.Unchecked += CheckBox_CheckedChanged;
            ChkMuros.Checked += CheckBox_CheckedChanged;
            ChkMuros.Unchecked += CheckBox_CheckedChanged;
            ChkVigas.Checked += CheckBox_CheckedChanged;
            ChkVigas.Unchecked += CheckBox_CheckedChanged;
            ChkLosas.Checked += CheckBox_CheckedChanged;
            ChkLosas.Unchecked += CheckBox_CheckedChanged;
            ChkEscaleras.Checked += CheckBox_CheckedChanged;
            ChkEscaleras.Unchecked += CheckBox_CheckedChanged;
            ChkCimentacion.Checked += CheckBox_CheckedChanged;
            ChkCimentacion.Unchecked += CheckBox_CheckedChanged;

            UpdateContinueButtonState();
        }

        private void CheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateContinueButtonState();
        }

        private void UpdateContinueButtonState()
        {
            bool anyChecked = ChkColumnas.IsChecked == true ||
                            ChkMuros.IsChecked == true ||
                            ChkVigas.IsChecked == true ||
                            ChkLosas.IsChecked == true ||
                            ChkEscaleras.IsChecked == true ||
                            ChkCimentacion.IsChecked == true;

            ContinueButton.IsEnabled = anyChecked;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            UserAccepted = false;
            DialogResult = false;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            UserAccepted = false;
            DialogResult = false;
            Close();
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            // Recopilar las categorías seleccionadas
            CategoriasSeleccionadas.Clear();

            if (ChkColumnas.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_StructuralColumns);

            if (ChkMuros.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_Walls);

            if (ChkVigas.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_StructuralFraming);

            if (ChkLosas.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_Floors);

            if (ChkEscaleras.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_Stairs);

            if (ChkCimentacion.IsChecked == true)
                CategoriasSeleccionadas.Add(BuiltInCategory.OST_StructuralFoundation);

            UserAccepted = true;
            DialogResult = true;
            Close();
        }
    }
}
