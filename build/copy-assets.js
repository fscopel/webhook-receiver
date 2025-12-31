/**
 * Copy Assets Script
 * Copies static assets (images, fonts, etc.) that don't need processing
 */

const fs = require("fs");
const path = require("path");
const config = require("./config");

function copyAssets() {
  console.log("Copying static assets...");

  if (config.copyAssets.length === 0) {
    console.log("  No assets configured to copy.");
    console.log("Assets copy complete!");
    return;
  }

  for (const asset of config.copyAssets) {
    const inputPath = path.join(config.SRC_DIR, asset);
    const outputPath = path.join(config.DIST_DIR, asset);

    if (!fs.existsSync(inputPath)) {
      console.warn(`Warning: ${inputPath} not found, skipping...`);
      continue;
    }

    console.log(`  Copying: ${asset}`);

    if (fs.statSync(inputPath).isDirectory()) {
      copyDir(inputPath, outputPath);
    } else {
      // Ensure directory exists
      const dir = path.dirname(outputPath);
      if (!fs.existsSync(dir)) {
        fs.mkdirSync(dir, { recursive: true });
      }
      fs.copyFileSync(inputPath, outputPath);
    }

    console.log(`    Output: ${outputPath}`);
  }

  console.log("Assets copy complete!");
}

function copyDir(src, dest) {
  if (!fs.existsSync(dest)) {
    fs.mkdirSync(dest, { recursive: true });
  }

  const entries = fs.readdirSync(src, { withFileTypes: true });

  for (const entry of entries) {
    const srcPath = path.join(src, entry.name);
    const destPath = path.join(dest, entry.name);

    if (entry.isDirectory()) {
      copyDir(srcPath, destPath);
    } else {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

copyAssets();
