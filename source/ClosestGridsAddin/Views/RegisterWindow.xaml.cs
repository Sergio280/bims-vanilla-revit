using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ClosestGridsAddinVANILLA.Services;

namespace ClosestGridsAddinVANILLA.Views
{
    public partial class RegisterWindow : Window
    {
        private readonly FirebaseAuthenticationService _authService;
        private readonly FirebaseLicenseService _licenseService;
        private readonly string _machineId;

        public bool RegisterSuccessful { get; private set; }
        public string UserId { get; private set; }
        public string UserEmail { get; private set; }

        public RegisterWindow()
        {
            InitializeComponent();

            _authService = new FirebaseAuthenticationService();
            _licenseService = new FirebaseLicenseService();
            _machineId = LicenseService.GetMachineId();

            // Foco en el campo de nombre al iniciar
            Loaded += (s, e) => NameTextBox.Focus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterSuccessful = false;
            DialogResult = false;
            Close();
        }

        private void InputField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateRegisterButtonState();
        }

        private void InputField_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateRegisterButtonState();
        }

        private void ConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && RegisterButton.IsEnabled)
            {
                RegisterButton_Click(sender, e);
            }
        }

        private void UpdateRegisterButtonState()
        {
            RegisterButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text) &&
                                      !string.IsNullOrWhiteSpace(EmailTextBox.Text) &&
                                      !string.IsNullOrEmpty(PasswordBox.Password) &&
                                      !string.IsNullOrEmpty(ConfirmPasswordBox.Password);
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var name = NameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            // Validaciones
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError("Por favor ingrese su nombre completo");
                return;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Por favor ingrese su correo electrónico");
                return;
            }

            // Validar formato de email
            if (!IsValidEmail(email))
            {
                ShowError("Por favor ingrese un correo electrónico válido");
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Por favor ingrese una contraseña");
                return;
            }

            if (password.Length < 6)
            {
                ShowError("La contraseña debe tener al menos 6 caracteres");
                return;
            }

            if (password != confirmPassword)
            {
                ShowError("Las contraseñas no coinciden");
                return;
            }

            // Mostrar loading
            ShowLoading(true);
            HideError();

            try
            {
                // Registrar en Firebase Authentication
                var authResult = await _authService.SignUpWithEmailAndPassword(email, password, name);

                if (!authResult.Success)
                {
                    ShowError(authResult.ErrorMessage);
                    ShowLoading(false);
                    return;
                }

                // Crear licencia de prueba en Realtime Database
                bool licenseCreated = await _licenseService.CreateTrialLicense(
                    authResult.UserId,
                    authResult.Email,
                    _machineId
                );

                if (!licenseCreated)
                {
                    ShowError("Error al crear la licencia. Por favor contacte al soporte.");
                    ShowLoading(false);
                    return;
                }

                // Registro exitoso
                RegisterSuccessful = true;
                UserId = authResult.UserId;
                UserEmail = authResult.Email;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError($"Error inesperado: {ex.Message}");
                ShowLoading(false);
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            RegisterSuccessful = false;
            DialogResult = false;
            Close();
        }

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Patrón regex para validar email
                var pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
                return Regex.IsMatch(email, pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = System.Windows.Visibility.Visible;
        }

        private void HideError()
        {
            ErrorTextBlock.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void ShowLoading(bool show)
        {
            LoadingOverlay.Visibility = show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            RegisterButton.IsEnabled = !show;
            NameTextBox.IsEnabled = !show;
            EmailTextBox.IsEnabled = !show;
            PasswordBox.IsEnabled = !show;
            ConfirmPasswordBox.IsEnabled = !show;
        }
    }
}
