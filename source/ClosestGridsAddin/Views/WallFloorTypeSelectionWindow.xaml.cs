using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClosestGridsAddinVANILLA.Views
{
    public partial class WallFloorTypeSelectionWindow : Window
    {
        public WallType SelectedWallType { get; private set; }
        public FloorType SelectedFloorType { get; private set; }
        public bool UserAccepted { get; private set; }

        public WallFloorTypeSelectionWindow(Document doc)
        {
            InitializeComponent();

            // Cargar tipos de muro
            var wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(wt => wt.Kind == WallKind.Basic) // Solo muros básicos
                .OrderBy(wt => wt.Name)
                .ToList();

            cmbWallType.ItemsSource = wallTypes;

            // Seleccionar "Encofrado 18mm" si existe, si no el primero
            var encofrado18 = wallTypes.FirstOrDefault(wt => wt.Name.Contains("Encofrado 18mm"));
            if (encofrado18 != null)
            {
                cmbWallType.SelectedItem = encofrado18;
            }
            else if (wallTypes.Any())
            {
                cmbWallType.SelectedIndex = 0;
            }

            // Cargar tipos de suelo
            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(ft => ft.Name)
                .ToList();

            cmbFloorType.ItemsSource = floorTypes;

            // Seleccionar "Cimbra 25mm" si existe, si no el primero
            var cimbra25 = floorTypes.FirstOrDefault(ft => ft.Name.Contains("Cimbra 25mm"));
            if (cimbra25 != null)
            {
                cmbFloorType.SelectedItem = cimbra25;
            }
            else if (floorTypes.Any())
            {
                cmbFloorType.SelectedIndex = 0;
            }

            UserAccepted = false;
        }

        private void btnAceptar_Click(object sender, RoutedEventArgs e)
        {
            // Validar selecciones
            if (cmbWallType.SelectedItem == null)
            {
                MessageBox.Show(
                    "Debe seleccionar un tipo de muro.",
                    "Validación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (cmbFloorType.SelectedItem == null)
            {
                MessageBox.Show(
                    "Debe seleccionar un tipo de suelo.",
                    "Validación",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Guardar selecciones
            SelectedWallType = cmbWallType.SelectedItem as WallType;
            SelectedFloorType = cmbFloorType.SelectedItem as FloorType;
            UserAccepted = true;

            DialogResult = true;
            Close();
        }

        private void btnCancelar_Click(object sender, RoutedEventArgs e)
        {
            UserAccepted = false;
            DialogResult = false;
            Close();
        }
    }
}
