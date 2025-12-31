/**
 * HTML Build Script
 * Minifies HTML files and updates references to point to minified assets
 */

const fs = require("fs");
const path = require("path");
const { minify } = require("html-minifier-terser");
const config = require("./config");

async function buildHtml() {
  console.log("Building HTML files...");

  for (const file of config.htmlFiles) {
    const inputPath = path.join(config.SRC_DIR, file);
    const outputPath = path.join(config.DIST_DIR, file);

    if (!fs.existsSync(inputPath)) {
      console.warn(`Warning: ${inputPath} not found, skipping...`);
      continue;
    }

    console.log(`  Processing: ${file}`);

    let source = fs.readFileSync(inputPath, "utf8");
    const originalSize = Buffer.byteLength(source, "utf8");

    // Minify HTML
    const minified = await minify(source, config.htmlMinifierOptions);
    const minifiedSize = Buffer.byteLength(minified, "utf8");

    console.log(
      `    Minified: ${formatBytes(originalSize)} -> ${formatBytes(
        minifiedSize
      )} (${getPercentChange(originalSize, minifiedSize)})`
    );

    fs.writeFileSync(outputPath, minified);
    console.log(`    Output: ${outputPath}`);
  }

  console.log("HTML build complete!");
}

function formatBytes(bytes) {
  if (bytes < 1024) return bytes + " B";
  return (bytes / 1024).toFixed(1) + " KB";
}

function getPercentChange(original, final) {
  const change = (((final - original) / original) * 100).toFixed(1);
  return change > 0 ? `+${change}%` : `${change}%`;
}

buildHtml().catch((err) => {
  console.error("HTML build failed:", err);
  process.exit(1);
});
