// ============================
// VARIABLES GLOBALES
// ============================
let currentUser = null;
let allLicenses = [];
let filteredLicenses = [];
let currentFilter = 'all';
let currentLicenseKey = null;

// ============================
// INICIALIZACIÓN
// ============================
document.addEventListener('DOMContentLoaded', () => {
    // Verificar si ya hay sesión activa
    firebase.auth().onAuthStateChanged(user => {
        if (user) {
            currentUser = user;
            showAdminPanel();
        } else {
            showLoginScreen();
        }
    });

    // Event Listeners
    setupEventListeners();
});

function setupEventListeners() {
    // Login
    document.getElementById('loginBtn').addEventListener('click', handleLogin);
    document.getElementById('adminPassword').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') handleLogin();
    });

    // Logout
    document.getElementById('logoutBtn').addEventListener('click', handleLogout);

    // Búsqueda
    document.getElementById('searchInput').addEventListener('input', handleSearch);

    // Filtros
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const filter = e.target.dataset.filter;
            setFilter(filter);
        });
    });

    // Refresh
    document.getElementById('refreshBtn').addEventListener('click', loadLicenses);

    // Modal
    document.getElementById('closeModal').addEventListener('click', closeModal);
    document.getElementById('cancelBtn').addEventListener('click', closeModal);
    document.getElementById('saveChangesBtn').addEventListener('click', saveLicenseChanges);
    document.getElementById('deleteLicenseBtn').addEventListener('click', deleteLicense);

    // Botones de agregar días
    document.querySelectorAll('.btn-days').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const days = parseInt(e.target.dataset.days);
            addDaysToExpiration(days);
        });
    });
}

// ============================
// AUTENTICACIÓN
// ============================
async function handleLogin() {
    const email = document.getElementById('adminEmail').value.trim();
    const password = document.getElementById('adminPassword').value;
    const errorDiv = document.getElementById('loginError');

    if (!email || !password) {
        errorDiv.textContent = 'Por favor ingrese email y contraseña';
        return;
    }

    try {
        showLoading(true);
        await firebase.auth().signInWithEmailAndPassword(email, password);
        errorDiv.textContent = '';
    } catch (error) {
        console.error('Error de login:', error);
        errorDiv.textContent = 'Credenciales inválidas: ' + error.message;
    } finally {
        showLoading(false);
    }
}

async function handleLogout() {
    try {
        await firebase.auth().signOut();
        showLoginScreen();
    } catch (error) {
        console.error('Error al cerrar sesión:', error);
        alert('Error al cerrar sesión');
    }
}

function showLoginScreen() {
    document.getElementById('loginScreen').classList.remove('hidden');
    document.getElementById('adminPanel').classList.add('hidden');
}

function showAdminPanel() {
    document.getElementById('loginScreen').classList.add('hidden');
    document.getElementById('adminPanel').classList.remove('hidden');
    document.getElementById('adminUserInfo').textContent = `👤 ${currentUser.email}`;
    loadLicenses();
}

// ============================
// CARGAR LICENCIAS
// ============================
async function loadLicenses() {
    try {
        showLoading(true);

        const licensesRef = firebase.database().ref('users');
        const snapshot = await licensesRef.once('value');

        allLicenses = [];

        snapshot.forEach(childSnapshot => {
            const license = childSnapshot.val();
            const licenseKey = childSnapshot.key;

            // Compatibilidad con camelCase y PascalCase
            const expirationDate = license.expirationDate || license.ExpirationDate;
            const isActive = license.isActive !== undefined ? license.isActive : license.IsActive;
            const email = license.email || license.Email;
            const licenseType = license.licenseType || license.LicenseType;

            allLicenses.push({
                key: licenseKey,
                ...license,
                Email: email,
                IsActive: isActive,
                ExpirationDate: expirationDate,
                LicenseType: licenseType,
                daysRemaining: calculateDaysRemaining(expirationDate),
                isExpired: new Date(expirationDate) < new Date()
            });
        });

        // Ordenar por fecha de expiración (más recientes primero)
        allLicenses.sort((a, b) => new Date(b.ExpirationDate) - new Date(a.ExpirationDate));

        updateStatistics();
        applyFilter();

    } catch (error) {
        console.error('Error al cargar licencias:', error);
        alert('Error al cargar licencias: ' + error.message);
    } finally {
        showLoading(false);
    }
}

function calculateDaysRemaining(expirationDate) {
    const now = new Date();
    const expiration = new Date(expirationDate);
    const diffTime = expiration - now;
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
    return Math.max(0, diffDays);
}

// ============================
// ESTADÍSTICAS
// ============================
function updateStatistics() {
    const total = allLicenses.length;
    const active = allLicenses.filter(l => l.IsActive && !l.isExpired).length;
    const expired = allLicenses.filter(l => l.isExpired).length;
    const trial = allLicenses.filter(l => l.LicenseType === 'Trial').length;

    document.getElementById('totalLicenses').textContent = total;
    document.getElementById('activeLicenses').textContent = active;
    document.getElementById('expiredLicenses').textContent = expired;
    document.getElementById('trialLicenses').textContent = trial;

    // Actualizar contadores en filtros
    document.getElementById('countAll').textContent = total;
    document.getElementById('countActive').textContent = active;
    document.getElementById('countExpired').textContent = expired;
    document.getElementById('countTrial').textContent = trial;
}

// ============================
// FILTROS Y BÚSQUEDA
// ============================
function setFilter(filter) {
    currentFilter = filter;

    // Actualizar botones
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.classList.remove('active');
    });
    document.querySelector(`[data-filter="${filter}"]`).classList.add('active');

    applyFilter();
}

function handleSearch(e) {
    const searchTerm = e.target.value.toLowerCase().trim();

    if (searchTerm === '') {
        applyFilter();
        return;
    }

    filteredLicenses = allLicenses.filter(license => {
        return (
            license.Email?.toLowerCase().includes(searchTerm) ||
            license.DisplayName?.toLowerCase().includes(searchTerm) ||
            license.LicenseId?.toLowerCase().includes(searchTerm) ||
            license.LicenseType?.toLowerCase().includes(searchTerm)
        );
    });

    renderTable();
}

function applyFilter() {
    switch (currentFilter) {
        case 'all':
            filteredLicenses = [...allLicenses];
            break;
        case 'active':
            filteredLicenses = allLicenses.filter(l => l.IsActive && !l.isExpired);
            break;
        case 'expired':
            filteredLicenses = allLicenses.filter(l => l.isExpired);
            break;
        case 'trial':
            filteredLicenses = allLicenses.filter(l => l.LicenseType === 'Trial');
            break;
    }

    renderTable();
}

// ============================
// RENDERIZAR TABLA
// ============================
function renderTable() {
    const tbody = document.getElementById('licensesTableBody');
    tbody.innerHTML = '';

    if (filteredLicenses.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8" style="text-align: center; padding: 40px; color: #94a3b8;">No se encontraron licencias</td></tr>';
        return;
    }

    filteredLicenses.forEach(license => {
        const row = document.createElement('tr');

        // Estado
        const statusBadge = license.IsActive && !license.isExpired
            ? '<span class="status-badge status-active">✅ Activa</span>'
            : '<span class="status-badge status-expired">❌ Expirada</span>';

        // Días restantes con color
        let daysColor = '#22c55e'; // Verde
        if (license.daysRemaining <= 7) daysColor = '#ef4444'; // Rojo
        else if (license.daysRemaining <= 30) daysColor = '#f59e0b'; // Amarillo

        row.innerHTML = `
            <td>${statusBadge}</td>
            <td>${license.Email || 'N/A'}</td>
            <td>${license.DisplayName || 'N/A'}</td>
            <td><span class="license-type">${license.LicenseType || 'N/A'}</span></td>
            <td>${formatDate(license.ExpirationDate)}</td>
            <td style="color: ${daysColor}; font-weight: 600;">${license.daysRemaining} días</td>
            <td>${license.ValidationCount || 0}</td>
            <td>
                <button class="btn-edit" onclick="editLicense('${license.key}')">
                    ✏️ Editar
                </button>
            </td>
        `;

        tbody.appendChild(row);
    });
}

function formatDate(dateString) {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    return date.toLocaleDateString('es-ES', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

// ============================
// EDITAR LICENCIA
// ============================
function editLicense(licenseKey) {
    currentLicenseKey = licenseKey;
    const license = allLicenses.find(l => l.key === licenseKey);

    if (!license) {
        alert('Licencia no encontrada');
        return;
    }

    // Llenar formulario
    document.getElementById('modalEmail').value = license.Email || '';
    document.getElementById('modalName').value = license.DisplayName || license.Email || '';
    document.getElementById('modalLicenseType').value = license.LicenseType || 'Trial';
    document.getElementById('modalIsActive').checked = license.IsActive;

    // Max Activations (compatibilidad con maxActivations o MaxDevices)
    const maxActivations = license.maxActivations || license.MaxDevices || license.MaxActivations || 2;
    document.getElementById('modalMaxActivations').value = maxActivations;

    // Mostrar activaciones actuales
    const activationsDiv = document.getElementById('modalActivations');
    const activations = license.activations || {};
    const activationCount = Object.keys(activations).length;

    if (activationCount === 0) {
        activationsDiv.innerHTML = '<span style="color: #94a3b8;">No hay activaciones registradas</span>';
    } else {
        let activationsHTML = `<div style="margin-bottom: 8px; color: #22c55e; font-weight: 600;">${activationCount} de ${maxActivations} equipos activados</div>`;

        Object.entries(activations).forEach(([hardwareId, info]) => {
            const activatedDate = info.activatedAt ? new Date(info.activatedAt).toLocaleDateString('es-ES') : 'N/A';
            const lastSeen = info.lastSeen ? new Date(info.lastSeen).toLocaleDateString('es-ES') : 'N/A';
            const machineName = info.machineName || 'Desconocido';

            activationsHTML += `
                <div style="border: 1px solid #334155; padding: 8px; margin-top: 6px; border-radius: 4px; background: #0f172a;">
                    <div style="color: #60a5fa; font-weight: 600;">🖥️ ${machineName}</div>
                    <div style="color: #94a3b8; font-size: 11px; margin-top: 4px;">
                        Activado: ${activatedDate} | Último uso: ${lastSeen}
                    </div>
                    <div style="color: #475569; font-size: 10px; margin-top: 2px; font-family: monospace;">
                        ID: ${hardwareId.substring(0, 16)}...
                    </div>
                </div>
            `;
        });

        activationsDiv.innerHTML = activationsHTML;
    }

    // Fecha de expiración en formato datetime-local
    if (license.ExpirationDate) {
        const date = new Date(license.ExpirationDate);
        const formattedDate = date.toISOString().slice(0, 16);
        document.getElementById('modalExpirationDate').value = formattedDate;
    }

    // Mostrar modal
    document.getElementById('editModal').classList.remove('hidden');
}

function closeModal() {
    document.getElementById('editModal').classList.add('hidden');
    currentLicenseKey = null;
}

function addDaysToExpiration(days) {
    const currentDateInput = document.getElementById('modalExpirationDate').value;
    let currentDate;

    if (currentDateInput) {
        currentDate = new Date(currentDateInput);
    } else {
        currentDate = new Date();
    }

    currentDate.setDate(currentDate.getDate() + days);
    const formattedDate = currentDate.toISOString().slice(0, 16);
    document.getElementById('modalExpirationDate').value = formattedDate;
}

async function saveLicenseChanges() {
    if (!currentLicenseKey) return;

    try {
        showLoading(true);

        const licenseType = document.getElementById('modalLicenseType').value;
        const expirationDate = new Date(document.getElementById('modalExpirationDate').value).toISOString();
        const isActive = document.getElementById('modalIsActive').checked;
        const maxActivations = parseInt(document.getElementById('modalMaxActivations').value) || 2;

        // Guardar en formato camelCase para compatibilidad
        const updates = {
            licenseType: licenseType,
            expirationDate: expirationDate,
            isActive: isActive,
            maxActivations: maxActivations,
            // Mantener PascalCase para compatibilidad con código antiguo
            LicenseType: licenseType,
            ExpirationDate: expirationDate,
            IsActive: isActive,
            MaxDevices: maxActivations
        };

        await firebase.database().ref(`users/${currentLicenseKey}`).update(updates);

        alert('✅ Licencia actualizada exitosamente');
        closeModal();
        loadLicenses();

    } catch (error) {
        console.error('Error al guardar cambios:', error);
        alert('❌ Error al guardar cambios: ' + error.message);
    } finally {
        showLoading(false);
    }
}

async function deleteLicense() {
    if (!currentLicenseKey) return;

    const license = allLicenses.find(l => l.key === currentLicenseKey);
    if (!license) return;

    const confirmed = confirm(`¿Está seguro de eliminar la licencia de ${license.Email}?\n\nEsta acción NO se puede deshacer.`);

    if (!confirmed) return;

    try {
        showLoading(true);

        await firebase.database().ref(`users/${currentLicenseKey}`).remove();

        alert('🗑️ Licencia eliminada exitosamente');
        closeModal();
        loadLicenses();

    } catch (error) {
        console.error('Error al eliminar licencia:', error);
        alert('❌ Error al eliminar licencia: ' + error.message);
    } finally {
        showLoading(false);
    }
}

// ============================
// UTILIDADES
// ============================
function showLoading(show) {
    const overlay = document.getElementById('loadingOverlay');
    if (show) {
        overlay.classList.remove('hidden');
    } else {
        overlay.classList.add('hidden');
    }
}

// Hacer funciones globales para onclick
window.editLicense = editLicense;
