// Webhook Receiver Dashboard

class WebhookDashboard {
  constructor() {
    this.connection = null;
    this.webhooks = new Map();
    this.searchTerm = "";
    this.user = null;

    this.init();
  }

  async init() {
    // Check authentication first
    const isAuthenticated = await this.checkAuthentication();
    if (!isAuthenticated) {
      return; // Will redirect to login
    }

    this.setupUI();
    this.setupAuthUI();
    await this.connectSignalR();
  }

  async checkAuthentication() {
    const authLoading = document.getElementById("authLoading");
    const app = document.getElementById("app");

    try {
      this.user = await authManager.checkAuth();

      if (!this.user) {
        // Not authenticated, redirect to login
        window.location.href = "/login.html";
        return false;
      }

      // Authenticated, show the app
      authLoading.style.display = "none";
      app.style.display = "flex";
      return true;
    } catch (error) {
      console.error("Auth check error:", error);
      window.location.href = "/login.html";
      return false;
    }
  }

  setupAuthUI() {
    // Display user email
    const userEmail = document.getElementById("userEmail");
    if (userEmail && this.user) {
      userEmail.textContent = this.user.email;
    }

    // Logout button
    const logoutBtn = document.getElementById("logoutBtn");
    if (logoutBtn) {
      logoutBtn.addEventListener("click", async () => {
        await authManager.signOut();
      });
    }
  }

  setupUI() {
    // Set webhook URL
    const baseUrl = window.location.origin;
    document.getElementById(
      "webhookUrl"
    ).textContent = `${baseUrl}/api/webhook`;

    // Copy URL button
    document.getElementById("copyUrlBtn").addEventListener("click", () => {
      navigator.clipboard.writeText(`${baseUrl}/api/webhook`);
      this.showToast("URL copied to clipboard!");
    });

    // Clear all button
    document.getElementById("clearAllBtn").addEventListener("click", () => {
      this.showConfirmDialog({
        title: "Clear All Webhooks?",
        message: "This will remove all webhooks from your view.",
        confirmText: "Clear All",
        onConfirm: () => this.clearAll(),
      });
    });

    // Restore all button
    document.getElementById("restoreAllBtn").addEventListener("click", () => {
      this.restoreAll();
    });

    // Search input
    document.getElementById("searchInput").addEventListener("input", (e) => {
      this.searchTerm = e.target.value.toLowerCase();
      this.renderWebhooks();
    });

    // Setup dialog event listeners
    this.setupDialog();
  }

  setupDialog() {
    const dialog = document.getElementById("confirmDialog");
    const cancelBtn = document.getElementById("dialogCancel");
    const confirmBtn = document.getElementById("dialogConfirm");

    // Store callback reference
    this.dialogCallback = null;

    // Close on cancel
    cancelBtn.addEventListener("click", () => this.hideConfirmDialog());

    // Confirm button - calls stored callback
    confirmBtn.addEventListener("click", () => {
      this.hideConfirmDialog();
      if (this.dialogCallback) {
        this.dialogCallback();
        this.dialogCallback = null;
      }
    });

    // Close on overlay click
    dialog.addEventListener("click", (e) => {
      if (e.target === dialog) {
        this.hideConfirmDialog();
      }
    });

    // Close on Escape key
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && dialog.classList.contains("active")) {
        this.hideConfirmDialog();
      }
    });
  }

  showConfirmDialog({ title, message, confirmText, onConfirm }) {
    const dialog = document.getElementById("confirmDialog");
    const titleEl = document.getElementById("dialogTitle");
    const messageEl = document.getElementById("dialogMessage");
    const confirmBtn = document.getElementById("dialogConfirm");

    titleEl.textContent = title;
    messageEl.textContent = message;
    confirmBtn.textContent = confirmText;

    // Store the callback
    this.dialogCallback = onConfirm;

    dialog.classList.add("active");
  }

  hideConfirmDialog() {
    const dialog = document.getElementById("confirmDialog");
    dialog.classList.remove("active");
  }

  async connectSignalR() {
    // Get Firebase token for server-side authentication
    const token = await authManager.getIdToken();

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("/webhookhub", {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Connection state handlers
    this.connection.onreconnecting(() => {
      this.updateConnectionStatus("reconnecting");
    });

    this.connection.onreconnected(() => {
      this.updateConnectionStatus("connected");
    });

    this.connection.onclose(() => {
      this.updateConnectionStatus("disconnected");
    });

    // Message handlers
    this.connection.on("InitialData", (entries) => {
      this.webhooks.clear();
      entries.forEach((entry) => {
        this.webhooks.set(entry.id, entry);
      });
      this.renderWebhooks();
    });

    this.connection.on("NewWebhook", (entry) => {
      this.webhooks.set(entry.id, entry);
      this.renderWebhooks();
      this.highlightNew(entry.id);
    });

    this.connection.on("EntryDeleted", (id) => {
      this.webhooks.delete(id);
      this.renderWebhooks();
    });

    this.connection.on("AllCleared", () => {
      this.webhooks.clear();
      this.renderWebhooks();
    });

    this.connection.on("AllRestored", (entries) => {
      this.webhooks.clear();
      entries.forEach((entry) => {
        this.webhooks.set(entry.id, entry);
      });
      this.renderWebhooks();
      this.showToast(`Restored ${entries.length} webhooks`);
    });

    // Start connection
    try {
      await this.connection.start();
      this.updateConnectionStatus("connected");
    } catch (err) {
      console.error("SignalR connection error:", err);
      this.updateConnectionStatus("disconnected");
      // Retry after 5 seconds
      setTimeout(() => this.connectSignalR(), 5000);
    }
  }

  updateConnectionStatus(status) {
    const statusEl = document.getElementById("connectionStatus");
    const textEl = statusEl.querySelector(".status-text");

    statusEl.className = "connection-status " + status;

    switch (status) {
      case "connected":
        textEl.textContent = "Connected";
        break;
      case "reconnecting":
        textEl.textContent = "Reconnecting...";
        break;
      case "disconnected":
        textEl.textContent = "Disconnected";
        break;
      default:
        textEl.textContent = "Connecting...";
    }
  }

  renderWebhooks() {
    const list = document.getElementById("webhooksList");
    const countEl = document.getElementById("webhookCount");

    // Filter webhooks
    const filtered = Array.from(this.webhooks.values())
      .filter((w) => this.matchesSearch(w))
      .sort((a, b) => new Date(b.receivedAt) - new Date(a.receivedAt));

    countEl.textContent = this.webhooks.size;

    if (filtered.length === 0) {
      list.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                            <path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/>
                        </svg>
                    </div>
                    <h3>No webhooks yet</h3>
                    <p>Send a request to your webhook URL to see it appear here in real-time.</p>
                </div>
            `;
      return;
    }

    // Build HTML
    const template = document.getElementById("webhookTemplate");
    list.innerHTML = "";

    filtered.forEach((webhook) => {
      const card = template.content.cloneNode(true);
      const cardEl = card.querySelector(".webhook-card");

      cardEl.dataset.id = webhook.id;

      // Method badge
      const methodBadge = card.querySelector(".method-badge");
      methodBadge.textContent = webhook.method;
      methodBadge.classList.add(webhook.method);

      // Path
      card.querySelector(".webhook-path").textContent = webhook.path;

      // Channel
      const channelEl = card.querySelector(".webhook-channel");
      if (webhook.channel) {
        channelEl.textContent = webhook.channel;
      }

      // Time
      card.querySelector(".webhook-time").textContent = this.formatTime(
        webhook.receivedAt
      );

      // Headers
      card.querySelector(".headers-content").textContent = JSON.stringify(
        webhook.headers,
        null,
        2
      );

      // Body
      const bodyContent = card.querySelector(".body-content");
      card.querySelector(".content-type").textContent =
        webhook.contentType || "";

      if (webhook.body) {
        try {
          const parsed = JSON.parse(webhook.body);
          bodyContent.textContent = JSON.stringify(parsed, null, 2);
        } catch {
          bodyContent.textContent = webhook.body;
        }
      } else {
        bodyContent.textContent = "(empty)";
        bodyContent.style.color = "var(--text-muted)";
      }

      // Meta
      card.querySelector(".webhook-id").textContent = webhook.id;
      card.querySelector(".source-ip").textContent = webhook.sourceIp || "N/A";
      card.querySelector(".content-length").textContent = this.formatBytes(
        webhook.contentLength
      );
      card.querySelector(".query-string").textContent =
        webhook.queryString || "(none)";

      // Toggle expand
      card.querySelector(".webhook-header").addEventListener("click", (e) => {
        if (!e.target.closest(".delete-btn")) {
          cardEl.classList.toggle("expanded");
        }
      });

      // Delete button
      card.querySelector(".delete-btn").addEventListener("click", (e) => {
        e.stopPropagation();
        this.deleteWebhook(webhook.id);
      });

      list.appendChild(card);
    });
  }

  matchesSearch(webhook) {
    if (!this.searchTerm) return true;

    const searchable = [
      webhook.method,
      webhook.path,
      webhook.channel,
      webhook.body,
      webhook.contentType,
      JSON.stringify(webhook.headers),
    ]
      .join(" ")
      .toLowerCase();

    return searchable.includes(this.searchTerm);
  }

  formatTime(dateStr) {
    const date = new Date(dateStr);
    const now = new Date();
    const diff = now - date;

    if (diff < 60000) {
      return "Just now";
    } else if (diff < 3600000) {
      const mins = Math.floor(diff / 60000);
      return `${mins}m ago`;
    } else if (diff < 86400000) {
      const hours = Math.floor(diff / 3600000);
      return `${hours}h ago`;
    } else {
      return date.toLocaleString();
    }
  }

  formatBytes(bytes) {
    if (bytes === 0) return "0 B";
    const k = 1024;
    const sizes = ["B", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
  }

  highlightNew(id) {
    const card = document.querySelector(`[data-id="${id}"]`);
    if (card) {
      card.style.background = "var(--accent-glow)";
      setTimeout(() => {
        card.style.background = "";
      }, 1000);
    }
  }

  async deleteWebhook(id) {
    try {
      await this.connection.invoke("DeleteEntry", id);
    } catch (err) {
      console.error("Delete error:", err);
      // Fallback to REST API with authentication
      await authManager.fetchWithAuth(`/api/webhooks/${id}`, {
        method: "DELETE",
      });
      this.webhooks.delete(id);
      this.renderWebhooks();
    }
  }

  async clearAll() {
    try {
      await this.connection.invoke("ClearAll");
    } catch (err) {
      console.error("Clear error:", err);
      // Fallback to REST API with authentication
      await authManager.fetchWithAuth("/api/webhooks", { method: "DELETE" });
      this.webhooks.clear();
      this.renderWebhooks();
    }
  }

  async restoreAll() {
    try {
      await this.connection.invoke("RestoreAll");
    } catch (err) {
      console.error("Restore error:", err);
      this.showToast("Failed to restore webhooks");
    }
  }

  showToast(message) {
    // Simple toast notification
    const toast = document.createElement("div");
    toast.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            background: var(--accent-primary);
            color: var(--bg-primary);
            padding: 0.75rem 1.5rem;
            border-radius: var(--radius-md);
            font-weight: 500;
            animation: slideUp 0.3s ease;
            z-index: 1000;
        `;
    toast.textContent = message;
    document.body.appendChild(toast);

    setTimeout(() => {
      toast.style.opacity = "0";
      toast.style.transition = "opacity 0.3s ease";
      setTimeout(() => toast.remove(), 300);
    }, 2000);
  }
}

// Add slideUp animation
const style = document.createElement("style");
style.textContent = `
    @keyframes slideUp {
        from {
            opacity: 0;
            transform: translateY(20px);
        }
        to {
            opacity: 1;
            transform: translateY(0);
        }
    }
`;
document.head.appendChild(style);

// Initialize dashboard
document.addEventListener("DOMContentLoaded", () => {
  window.dashboard = new WebhookDashboard();
});
