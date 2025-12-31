// Firebase Authentication for Webhook Receiver
// Passwordless email link authentication with domain restriction

const firebaseConfig = {
  apiKey: "AIzaSyAe2d2XlZWTDd7baOSgZLwwHqmrDMNhqFo",
  authDomain: "webhook-receiver-ldeat.firebaseapp.com",
  projectId: "webhook-receiver-ldeat",
  storageBucket: "webhook-receiver-ldeat.firebasestorage.app",
  messagingSenderId: "571142452256",
  appId: "1:571142452256:web:eb06788a17ef1ac4b3b845",
};

// Initialize Firebase
firebase.initializeApp(firebaseConfig);
const auth = firebase.auth();

// Configuration - email validation moved to server-side
const AUTH_CONFIG = {
  sessionDurationHours: 48,
  storageKeys: {
    loginTime: "webhook_login_time",
    userEmail: "webhook_user_email",
    emailForSignIn: "emailForSignIn",
  },
};

// Auth Manager Class
class AuthManager {
  constructor() {
    this.currentUser = null;
  }

  // Validate email on server - returns { valid: boolean, error?: string }
  async validateEmailOnServer(email) {
    try {
      const response = await fetch("/api/auth/validate-email", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });

      const data = await response.json();
      return data;
    } catch (error) {
      console.error("Server validation error:", error);
      return {
        valid: false,
        error: "Unable to validate email. Please try again.",
      };
    }
  }

  // Check if session is still valid (within 48 hours)
  isSessionValid() {
    const loginTime = localStorage.getItem(AUTH_CONFIG.storageKeys.loginTime);
    if (!loginTime) return false;

    const loginDate = new Date(parseInt(loginTime));
    const now = new Date();
    const hoursDiff = (now - loginDate) / (1000 * 60 * 60);

    return hoursDiff < AUTH_CONFIG.sessionDurationHours;
  }

  // Save session info
  saveSession(email) {
    localStorage.setItem(
      AUTH_CONFIG.storageKeys.loginTime,
      Date.now().toString()
    );
    localStorage.setItem(AUTH_CONFIG.storageKeys.userEmail, email);
  }

  // Clear session
  clearSession() {
    localStorage.removeItem(AUTH_CONFIG.storageKeys.loginTime);
    localStorage.removeItem(AUTH_CONFIG.storageKeys.userEmail);
    localStorage.removeItem(AUTH_CONFIG.storageKeys.emailForSignIn);
  }

  // Get stored email
  getStoredEmail() {
    return localStorage.getItem(AUTH_CONFIG.storageKeys.userEmail);
  }

  // Send sign-in link
  async sendSignInLink(email) {
    const actionCodeSettings = {
      url: window.location.origin + "/login.html",
      handleCodeInApp: true,
    };

    await auth.sendSignInLinkToEmail(email, actionCodeSettings);
    localStorage.setItem(AUTH_CONFIG.storageKeys.emailForSignIn, email);
  }

  // Complete sign-in from email link
  async completeSignIn() {
    if (!auth.isSignInWithEmailLink(window.location.href)) {
      return null;
    }

    const email = localStorage.getItem(AUTH_CONFIG.storageKeys.emailForSignIn);

    if (!email) {
      // Email not in storage - user opened link in different browser/device
      // They need to start the sign-in process again from this browser
      throw new Error(
        "Please start the sign-in process from this browser by entering your email below."
      );
    }

    const result = await auth.signInWithEmailLink(email, window.location.href);
    localStorage.removeItem(AUTH_CONFIG.storageKeys.emailForSignIn);

    return result.user;
  }

  // Sign out
  async signOut() {
    await auth.signOut();
    this.clearSession();
    window.location.href = "/login.html";
  }

  // Check authentication state
  // Note: Email authorization is enforced server-side via FirebaseAuthMiddleware
  async checkAuth() {
    return new Promise((resolve) => {
      auth.onAuthStateChanged((user) => {
        if (user && this.isSessionValid()) {
          this.currentUser = user;
          resolve(user);
        } else {
          if (user) {
            // User exists but session expired
            this.signOut();
          }
          resolve(null);
        }
      });
    });
  }

  // Get Firebase ID token for server-side validation
  async getIdToken() {
    if (!this.currentUser) return null;
    try {
      return await this.currentUser.getIdToken();
    } catch (error) {
      console.error("Error getting ID token:", error);
      return null;
    }
  }

  // Make authenticated API request
  async fetchWithAuth(url, options = {}) {
    const token = await this.getIdToken();
    if (!token) {
      throw new Error("Not authenticated");
    }

    const headers = {
      ...options.headers,
      Authorization: `Bearer ${token}`,
    };

    return fetch(url, { ...options, headers });
  }
}

// Create global auth manager instance
const authManager = new AuthManager();

// Login page specific logic
if (
  window.location.pathname.includes("login.html") ||
  window.location.pathname === "/login.html"
) {
  document.addEventListener("DOMContentLoaded", async () => {
    const emailStep = document.getElementById("emailStep");
    const sentStep = document.getElementById("sentStep");
    const processingStep = document.getElementById("processingStep");
    const emailForm = document.getElementById("emailForm");
    const emailInput = document.getElementById("emailInput");
    const emailError = document.getElementById("emailError");
    const sendLinkBtn = document.getElementById("sendLinkBtn");
    const tryAgainBtn = document.getElementById("tryAgainBtn");
    const sentEmail = document.getElementById("sentEmail");

    // Check if this is a sign-in link callback
    if (auth.isSignInWithEmailLink(window.location.href)) {
      showStep("processing");

      try {
        const user = await authManager.completeSignIn();
        if (user) {
          authManager.saveSession(user.email);
          // Redirect to main app
          window.location.href = "/";
        }
      } catch (error) {
        console.error("Sign-in error:", error);
        showError(error.message || "Failed to sign in. Please try again.");
        showStep("email");
      }
      return;
    }

    // Check if already authenticated
    const user = await authManager.checkAuth();
    if (user) {
      window.location.href = "/";
      return;
    }

    // Handle email form submission
    emailForm.addEventListener("submit", async (e) => {
      e.preventDefault();
      hideError();

      const email = emailInput.value.trim().toLowerCase();

      // Validate email is not empty
      if (!email) {
        showError("Please enter your email address");
        return;
      }

      setLoading(true);

      try {
        // Validate email on server BEFORE sending Firebase link
        const validation = await authManager.validateEmailOnServer(email);

        if (!validation.valid) {
          showError(validation.error || "This email is not authorized.");
          setLoading(false);
          return;
        }

        // Server approved - now send Firebase sign-in link
        await authManager.sendSignInLink(email);
        sentEmail.textContent = email;
        showStep("sent");
      } catch (error) {
        console.error("Send link error:", error);
        showError(getErrorMessage(error));
      } finally {
        setLoading(false);
      }
    });

    // Handle try again button
    tryAgainBtn.addEventListener("click", () => {
      emailInput.value = "";
      showStep("email");
    });

    // Helper functions
    function showStep(step) {
      emailStep.style.display = step === "email" ? "block" : "none";
      sentStep.style.display = step === "sent" ? "block" : "none";
      processingStep.style.display = step === "processing" ? "block" : "none";
    }

    function showError(message) {
      emailError.textContent = message;
      emailError.style.display = "block";
    }

    function hideError() {
      emailError.style.display = "none";
    }

    function setLoading(loading) {
      const btnText = sendLinkBtn.querySelector(".btn-text");
      const btnLoading = sendLinkBtn.querySelector(".btn-loading");

      sendLinkBtn.disabled = loading;
      btnText.style.display = loading ? "none" : "inline";
      btnLoading.style.display = loading ? "inline-flex" : "none";
    }

    function getErrorMessage(error) {
      switch (error.code) {
        case "auth/invalid-email":
          return "Please enter a valid email address";
        case "auth/quota-exceeded":
          return "Too many requests. Please try again later";
        case "auth/network-request-failed":
          return "Network error. Please check your connection";
        default:
          return error.message || "An error occurred. Please try again";
      }
    }
  });
}
