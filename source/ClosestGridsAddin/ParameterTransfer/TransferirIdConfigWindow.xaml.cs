using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClosestGridsAddinVANILLA.ParameterTransfer
{
    public partial class TransferirIdConfigWindow : Window
    {
        public string ParameterName { get; private set; }
        public BuiltInCategory? SelectedCategory { get; private set; }
        public bool ProcessOnlySelection { get; private set; }

        private Dictionary<string, BuiltInCategory> _categoryMap;

        public TransferirIdConfigWindow()
        {
            InitializeComponent();
            InitializeCategoryComboBox();
            txtParameterName.Text = "OIP_ID_BIM"; // Valor por defecto
            txtParameterName.Focus();
            txtParameterName.SelectAll();
        }

        private void InitializeCategoryComboBox()
        {
            _categoryMap = new Dictionary<string, BuiltInCategory>
            {
                { "-- Todas las categorías --", (BuiltInCategory)0 }, // Valor especial para "todas"
                { "Muros", BuiltInCategory.OST_Walls },
                { "Suelos", BuiltInCategory.OST_Floors },
                { "Techos", BuiltInCategory.OST_Roofs },
                { "Puertas", BuiltInCategory.OST_Doors },
                { "Ventanas", BuiltInCategory.OST_Windows },
                { "Columnas estructurales", BuiltInCategory.OST_StructuralColumns },
                { "Vigas estructurales", BuiltInCategory.OST_StructuralFraming },
                { "Cimentaciones", BuiltInCategory.OST_StructuralFoundation },
                { "Escaleras", BuiltInCategory.OST_Stairs },
                { "Barandillas", BuiltInCategory.OST_Railings },
                { "Mobiliario", BuiltInCategory.OST_Furniture },
                { "Equipos especiales", BuiltInCategory.OST_SpecialityEquipment },
                { "Modelo genérico", BuiltInCategory.OST_GenericModel },
                { "Aparatos de fontanería", BuiltInCategory.OST_PlumbingFixtures },
                { "Aparatos eléctricos", BuiltInCategory.OST_ElectricalEquipment },
                { "Equipos mecánicos", BuiltInCategory.OST_MechanicalEquipment },
                { "Luminarias", BuiltInCategory.OST_ElectricalFixtures },
                { "Conductos", BuiltInCategory.OST_DuctCurves },
                { "Tuberías", BuiltInCategory.OST_PipeCurves },
                { "Bandejas de cables", BuiltInCategory.OST_CableTray },
                { "Habitaciones", BuiltInCategory.OST_Rooms },
                { "Áreas", BuiltInCategory.OST_Areas },
                { "Rejillas", BuiltInCategory.OST_Grids },
                { "Niveles", BuiltInCategory.OST_Levels }
            };

            cmbCategory.ItemsSource = _categoryMap.Keys.ToList();
            cmbCategory.SelectedIndex = 0; // Seleccionar "Todas las categorías" por defecto
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // Validar nombre del parámetro
            if (string.IsNullOrWhiteSpace(txtParameterName.Text))
            {
                MessageBox.Show(
                    "Por favor, ingrese el nombre del parámetro destino.",
                    "Campo requerido",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                txtParameterName.Focus();
                return;
            }

            ParameterName = txtParameterName.Text.Trim();
            ProcessOnlySelection = chkProcessSelection.IsChecked == true;

            // Obtener categoría seleccionada
            string selectedKey = cmbCategory.SelectedItem as string;
            if (selectedKey != null && _categoryMap.ContainsKey(selectedKey))
            {
                BuiltInCategory category = _categoryMap[selectedKey];
                if ((int)category == 0) // "Todas las categorías"
                {
                    SelectedCategory = null;
                }
                else
                {
                    SelectedCategory = category;
                }
            }
            else
            {
                SelectedCategory = null;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
