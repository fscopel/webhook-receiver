const functions = require("firebase-functions");
const admin = require("firebase-admin");

admin.initializeApp();
const db = admin.firestore();

/**
 * Scheduled function that runs every hour to clean up expired webhooks.
 * Deletes webhooks older than 24 hours from both:
 * - Master collection (webhooks)
 * - All user subcollections (user_webhooks/{email}/webhooks)
 */
exports.cleanupExpiredWebhooks = functions.pubsub
    .schedule("every 1 hours")
    .onRun(async (context) => {
      const now = admin.firestore.Timestamp.now();

      console.log("Starting webhook cleanup at", now.toDate().toISOString());

      // Step 1: Find and delete expired webhooks from master collection
      const masterCollection = db.collection("webhooks");
      const expiredMasterQuery = masterCollection
          .where("ExpiresAt", "<", now.toDate());
      const expiredMasterSnapshot = await expiredMasterQuery.get();

      const expiredIds = [];
      let masterDeleted = 0;

      // Delete from master in batches
      const masterBatch = db.batch();
      expiredMasterSnapshot.forEach((doc) => {
        expiredIds.push(doc.id);
        masterBatch.delete(doc.ref);
        masterDeleted++;
      });

      if (masterDeleted > 0) {
        await masterBatch.commit();
        console.log(`Deleted ${masterDeleted} expired webhooks from master`);
      }

      // Step 2: Delete the same IDs from all user subcollections
      if (expiredIds.length === 0) {
        console.log("No expired webhooks found");
        return null;
      }

      // Get all user documents
      const userWebhooksCollection = db.collection("user_webhooks");
      const usersSnapshot = await userWebhooksCollection.listDocuments();

      let userDeleted = 0;

      for (const userDoc of usersSnapshot) {
        const userWebhooks = userDoc.collection("webhooks");
        const batch = db.batch();
        let batchCount = 0;

        for (const id of expiredIds) {
          const webhookDoc = userWebhooks.doc(id);
          const exists = await webhookDoc.get();
          if (exists.exists) {
            batch.delete(webhookDoc);
            batchCount++;
          }
        }

        if (batchCount > 0) {
          await batch.commit();
          userDeleted += batchCount;
        }
      }

      console.log(`Deleted ${userDeleted} expired webhooks from user stores`);
      console.log(`Cleanup complete: ${masterDeleted} from master, ${userDeleted} from users`);

      return null;
    });

/**
 * HTTP function to manually trigger cleanup (for testing)
 * Call: https://<region>-<project>.cloudfunctions.net/manualCleanup
 */
exports.manualCleanup = functions.https.onRequest(async (req, res) => {
  // Simple auth check - require a secret header
  const authHeader = req.headers["x-cleanup-secret"];
  const expectedSecret = process.env.CLEANUP_SECRET || "webhook-cleanup-2024";

  if (authHeader !== expectedSecret) {
    res.status(401).send("Unauthorized");
    return;
  }

  const now = admin.firestore.Timestamp.now();
  console.log("Manual cleanup triggered at", now.toDate().toISOString());

  // Same logic as scheduled function
  const masterCollection = db.collection("webhooks");
  const expiredMasterQuery = masterCollection
      .where("ExpiresAt", "<", now.toDate());
  const expiredMasterSnapshot = await expiredMasterQuery.get();

  const expiredIds = [];
  let masterDeleted = 0;

  const masterBatch = db.batch();
  expiredMasterSnapshot.forEach((doc) => {
    expiredIds.push(doc.id);
    masterBatch.delete(doc.ref);
    masterDeleted++;
  });

  if (masterDeleted > 0) {
    await masterBatch.commit();
  }

  let userDeleted = 0;
  if (expiredIds.length > 0) {
    const userWebhooksCollection = db.collection("user_webhooks");
    const usersSnapshot = await userWebhooksCollection.listDocuments();

    for (const userDoc of usersSnapshot) {
      const userWebhooks = userDoc.collection("webhooks");
      const batch = db.batch();
      let batchCount = 0;

      for (const id of expiredIds) {
        const webhookDoc = userWebhooks.doc(id);
        const exists = await webhookDoc.get();
        if (exists.exists) {
          batch.delete(webhookDoc);
          batchCount++;
        }
      }

      if (batchCount > 0) {
        await batch.commit();
        userDeleted += batchCount;
      }
    }
  }

  res.json({
    success: true,
    masterDeleted,
    userDeleted,
    expiredIds: expiredIds.length,
    timestamp: now.toDate().toISOString(),
  });
});

