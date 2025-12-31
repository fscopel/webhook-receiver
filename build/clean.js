/**
 * Clean Script
 * Removes the dist folder before building
 */

const fs = require("fs");
const path = require("path");
const config = require("./config");

function removeDir(dir) {
  if (fs.existsSync(dir)) {
    fs.rmSync(dir, { recursive: true, force: true });
    console.log(`Removed: ${dir}`);
  }
}

function ensureDir(dir) {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
    console.log(`Created: ${dir}`);
  }
}

console.log("Cleaning build output...");
removeDir(config.DIST_DIR);

// Create fresh directory structure
ensureDir(config.DIST_DIR);
ensureDir(path.join(config.DIST_DIR, "js"));
ensureDir(path.join(config.DIST_DIR, "css"));

console.log("Clean complete!");
