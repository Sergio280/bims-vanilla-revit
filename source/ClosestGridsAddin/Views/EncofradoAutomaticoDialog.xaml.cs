using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClosestGridsAddinVANILLA.Views
{
    public partial class EncofradoAutomaticoDialog : Window
    {
        private Document _doc;

        public WallType WallTypeSeleccionado { get; private set; }
        public FloorType FloorTypeSeleccionado { get; private set; }

        public EncofradoAutomaticoDialog(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            CargarTiposMuro();
            CargarTiposSuelo();
        }

        private void CargarTiposMuro()
        {
            var wallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(wt => wt.Kind == WallKind.Basic)
                .OrderBy(wt => wt.Name)
                .ToList();

            cmbWallType.ItemsSource = wallTypes;

            // Pre-seleccionar "Encofrado" si existe
            var encofradoType = wallTypes.FirstOrDefault(wt =>
                wt.Name.Contains("Encofrado") ||
                wt.Name.Contains("encofrado") ||
                wt.Name.Contains("18mm"));

            if (encofradoType != null)
            {
                cmbWallType.SelectedItem = encofradoType;
            }
            else if (wallTypes.Count > 0)
            {
                cmbWallType.SelectedIndex = 0;
            }
        }

        private void CargarTiposSuelo()
        {
            var floorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(ft => ft.Name)
                .ToList();

            cmbFloorType.ItemsSource = floorTypes;

            // Pre-seleccionar "Cimbra" si existe
            var cimbraType = floorTypes.FirstOrDefault(ft =>
                ft.Name.Contains("Cimbra") ||
                ft.Name.Contains("cimbra") ||
                ft.Name.Contains("Encofrado") ||
                ft.Name.Contains("25mm"));

            if (cimbraType != null)
            {
                cmbFloorType.SelectedItem = cimbraType;
            }
            else if (floorTypes.Count > 0)
            {
                cmbFloorType.SelectedIndex = 0;
            }
        }

        private void CmbWallType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            WallTypeSeleccionado = cmbWallType.SelectedItem as WallType;

            if (WallTypeSeleccionado != null)
            {
                // Mostrar información del espesor
                double espesor = ObtenerEspesorMuro(WallTypeSeleccionado);
                txtWallTypeInfo.Text = $"Espesor: {(espesor * 304.8):F1} mm ({(espesor * 12):F2} in)";
            }

            ValidarSeleccion();
        }

        private void CmbFloorType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            FloorTypeSeleccionado = cmbFloorType.SelectedItem as FloorType;

            if (FloorTypeSeleccionado != null)
            {
                // Mostrar información del espesor
                double espesor = ObtenerEspesorSuelo(FloorTypeSeleccionado);
                txtFloorTypeInfo.Text = $"Espesor: {(espesor * 304.8):F1} mm ({(espesor * 12):F2} in)";
            }

            ValidarSeleccion();
        }

        private double ObtenerEspesorMuro(WallType wallType)
        {
            try
            {
                CompoundStructure structure = wallType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
            }
            catch { }

            return 0.0;
        }

        private double ObtenerEspesorSuelo(FloorType floorType)
        {
            try
            {
                CompoundStructure structure = floorType.GetCompoundStructure();
                if (structure != null)
                {
                    return structure.GetWidth();
                }
            }
            catch { }

            return 0.0;
        }

        private void ValidarSeleccion()
        {
            btnAceptar.IsEnabled = WallTypeSeleccionado != null && FloorTypeSeleccionado != null;
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
