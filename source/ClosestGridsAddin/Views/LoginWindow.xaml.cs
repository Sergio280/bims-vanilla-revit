using System;
using System.Windows;
using System.Windows.Input;
using ClosestGridsAddinVANILLA.Services;

namespace ClosestGridsAddinVANILLA.Views
{
    public partial class LoginWindow : Window
    {
        private readonly FirebaseAuthenticationService _authService;
        private readonly FirebaseLicenseService _licenseService;
        private readonly string _machineId;

        public bool LoginSuccessful { get; private set; }
        public string UserId { get; private set; }
        public string UserEmail { get; private set; }
        public string UserDisplayName { get; private set; }
        public string RefreshToken { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();

            _authService = new FirebaseAuthenticationService();
            _licenseService = new FirebaseLicenseService();
            _machineId = LicenseService.GetMachineId();

            // Foco en el campo de email al iniciar
            Loaded += (s, e) => EmailTextBox.Focus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            LoginSuccessful = false;
            DialogResult = false;
            Close();
        }

        private void EmailTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateLoginButtonState();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && LoginButton.IsEnabled)
            {
                LoginButton_Click(sender, e);
            }
            UpdateLoginButtonState();
        }

        private void UpdateLoginButtonState()
        {
            LoginButton.IsEnabled = !string.IsNullOrWhiteSpace(EmailTextBox.Text) &&
                                   !string.IsNullOrEmpty(PasswordBox.Password);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text.Trim();
            var password = PasswordBox.Password;

            // Validaciones básicas
            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Por favor ingrese su correo electrónico");
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Por favor ingrese su contraseña");
                return;
            }

            // Mostrar loading
            ShowLoading(true);
            HideError();

            try
            {
                // Autenticar con Firebase Authentication
                var authResult = await _authService.SignInWithEmailAndPassword(email, password);

                if (!authResult.Success)
                {
                    ShowError(authResult.ErrorMessage);
                    ShowLoading(false);
                    return;
                }

                // Validar licencia en Realtime Database
                var licenseResult = await _licenseService.ValidateLicense(authResult.UserId, _machineId);

                if (!licenseResult.IsValid)
                {
                    ShowError(licenseResult.Message);
                    ShowLoading(false);
                    return;
                }

                // Preparar datos de sesión
                var session = new SessionData
                {
                    UserId = authResult.UserId,
                    Email = authResult.Email,
                    DisplayName = authResult.DisplayName ?? authResult.Email,
                    RefreshToken = authResult.RefreshToken,
                    MachineId = _machineId,
                    SavedAt = DateTime.Now
                };

                // Guardar sesión en caché de memoria (PRIORIDAD)
                SessionCache.SetSession(session);
                System.Diagnostics.Debug.WriteLine($"Sesión guardada en caché: {session.Email}");

                // Intentar guardar sesión localmente con verificación
                if (!SessionManager.SaveSession(session, out string saveError))
                {
                    // Advertir pero continuar - el login fue exitoso aunque no se guardó la sesión en disco
                    System.Diagnostics.Debug.WriteLine($"Advertencia al guardar sesión: {saveError}");
                    // No mostrar mensaje al usuario, la sesión en caché es suficiente
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Sesión guardada en disco exitosamente");
                }

                // Guardar datos en propiedades públicas para compatibilidad
                LoginSuccessful = true;
                UserId = authResult.UserId;
                UserEmail = authResult.Email;
                UserDisplayName = authResult.DisplayName ?? authResult.Email;
                RefreshToken = authResult.RefreshToken;

                MessageBox.Show(
                    $"Bienvenido, {UserDisplayName}!\n\n{licenseResult.Message}",
                    "Inicio de sesión exitoso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError($"Error inesperado: {ex.Message}");
                ShowLoading(false);
            }
        }

        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            var registerWindow = new RegisterWindow();
            if (registerWindow.ShowDialog() == true)
            {
                // Si el registro fue exitoso, usar esas credenciales para login
                EmailTextBox.Text = registerWindow.UserEmail;
                MessageBox.Show(
                    "¡Registro exitoso! Se ha creado una licencia de prueba por 30 días.\nAhora puede iniciar sesión.",
                    "Registro completado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show(
                    "Por favor ingrese su correo electrónico en el campo correspondiente.",
                    "Recuperar contraseña",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            var result = MessageBox.Show(
                $"¿Desea enviar un correo de recuperación de contraseña a:\n{email}?",
                "Recuperar contraseña",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                ShowLoading(true);

                try
                {
                    bool sent = await _authService.SendPasswordResetEmail(email);

                    if (sent)
                    {
                        MessageBox.Show(
                            "Se ha enviado un correo de recuperación de contraseña.\nPor favor revise su bandeja de entrada.",
                            "Correo enviado",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            "No se pudo enviar el correo de recuperación.\nVerifique que el correo electrónico sea correcto.",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                    }
                }
                finally
                {
                    ShowLoading(false);
                }
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
            LoginButton.IsEnabled = !show;
            EmailTextBox.IsEnabled = !show;
            PasswordBox.IsEnabled = !show;
        }
    }
}
