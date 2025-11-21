

// Import the functions you need from the SDKs you need
import { initializeApp } from "https://www.gstatic.com/firebasejs/9.15.0/firebase-app.js";
import { getAuth, createUserWithEmailAndPassword, signInWithEmailAndPassword, signOut, sendPasswordResetEmail, onAuthStateChanged } from "https://www.gstatic.com/firebasejs/9.15.0/firebase-auth.js";

// Your web app's Firebase configuration
// This is safe to expose on the client side.
const firebaseConfig = {
    apiKey: "YOUR_WEB_API_KEY",
    authDomain: "YOUR_PROJECT_ID.firebaseapp.com",
    projectId: "YOUR_FIRESTORE_PROJECT_ID",
    storageBucket: "YOUR_PROJECT_ID.appspot.com",
};

// Initialize Firebase
const app = initializeApp(firebaseConfig);
const auth = getAuth(app);

// --- Cookie Management ---
function setCookie(name, value, days) {
    let expires = "";
    if (days) {
        const date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = "; expires=" + date.toUTCString();
    }
    // Set cookie to be accessible from the entire site
    document.cookie = name + "=" + (value || "") + expires + "; path=/";
}

function eraseCookie(name) {
    document.cookie = name + '=; Path=/; Expires=Thu, 01 Jan 1970 00:00:01 GMT;';
}

// --- Firebase Auth Functions ---

// Function to handle user registration
window.registerUser = async function (email, password, displayName) {
    try {
        const userCredential = await createUserWithEmailAndPassword(auth, email, password);
        const user = userCredential.user;

        // After creating the user in Firebase Auth, we need to create their profile in our Firestore DB.
        // We do this by calling a handler on our Register Razor Page.
        const response = await fetch('/Account/Register?handler=CreateUserDocument', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': document.getElementsByName('__RequestVerificationToken')[0].value
            },
            body: JSON.stringify({
                uid: user.uid,
                email: user.email,
                name: displayName
            })
        });

        if (!response.ok) {
            throw new Error('Server-side user creation failed.');
        }

        // After successful registration and profile creation, log the user in to set the cookie.
        await window.loginUser(email, password);

    } catch (error) {
        console.error("Registration failed:", error);
        alert(`Registration failed: ${error.message}`);
    }
};

// Function to handle user login
window.loginUser = async function (email, password) {
    try {
        const userCredential = await signInWithEmailAndPassword(auth, email, password);
        const user = userCredential.user;
        const idToken = await user.getIdToken();

        // Store the token in a cookie for the server-side middleware to read
        setCookie('firebaseToken', idToken, 1); // Cookie expires in 1 day

        // Redirect to home page after successful login
        window.location.href = '/';

    } catch (error) {
        console.error("Login failed:", error);
        eraseCookie('firebaseToken');
        alert(`Login failed: ${error.message}`);
    }
};

// Function to handle user logout
window.logoutUser = async function () {
    try {
        await signOut(auth);
        // Erase the cookie and redirect
        eraseCookie('firebaseToken');
        window.location.href = '/';
    } catch (error) {
        console.error("Logout failed:", error);
        alert(`Logout failed: ${error.message}`);
    }
};

// Function to send a password reset email
window.resetPassword = async function (email) {
    try {
        await sendPasswordResetEmail(auth, email);
        alert('Password reset email sent! Please check your inbox.');
        return true;
    } catch (error) {
        console.error("Password reset failed:", error);
        alert(`Password reset failed: ${error.message}`);
        return false;
    }
};

// Listen for auth state changes to keep the cookie fresh
onAuthStateChanged(auth, async (user) => {
    if (user) {
        // User is signed in. Get the token and update the cookie.
        const idToken = await user.getIdToken(true); // Force refresh
        setCookie('firebaseToken', idToken, 1);
    } else {
        // User is signed out.
        eraseCookie('firebaseToken');
    }
});