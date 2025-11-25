// ============================
// CONFIGURACIÓN DE FIREBASE
// ============================

const firebaseConfig = {
    apiKey: "AIzaSyDHReu2GQRuUJTi4ygonBNzEhLL_6P9B5E",
    authDomain: "bims-8d507.firebaseapp.com",
    databaseURL: "https://bims-8d507-default-rtdb.firebaseio.com",
    projectId: "bims-8d507",
    storageBucket: "bims-8d507.firebasestorage.app",
    messagingSenderId: "997600139423",
    appId: "1:997600139423:web:bb1e71022176358e97aaaa",
    measurementId: "G-P5ZL4FBL4S"
};

// Inicializar Firebase
firebase.initializeApp(firebaseConfig);

console.log('✅ Firebase inicializado correctamente');

// Exportar para uso global
window.firebaseConfig = firebaseConfig;
