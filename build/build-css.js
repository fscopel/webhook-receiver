/**
 * CSS Build Script
 * Minifies CSS files with clean-css
 */

const fs = require("fs");
const path = require("path");
const CleanCSS = require("clean-css");
const config = require("./config");

function buildCss() {
  console.log("Building CSS files...");

  const cleanCss = new CleanCSS(config.cleanCssOptions);

  for (const file of config.cssFiles) {
    const inputPath = path.join(config.SRC_DIR, file);
    const outputPath = path.join(config.DIST_DIR, file);

    if (!fs.existsSync(inputPath)) {
      console.warn(`Warning: ${inputPath} not found, skipping...`);
      continue;
    }

    console.log(`  Processing: ${file}`);

    const source = fs.readFileSync(inputPath, "utf8");
    const originalSize = Buffer.byteLength(source, "utf8");

    const output = cleanCss.minify(source);

    if (output.errors.length > 0) {
      console.error(`    Errors:`, output.errors);
      continue;
    }

    const minifiedSize = Buffer.byteLength(output.styles, "utf8");
    console.log(
      `    Minified: ${formatBytes(originalSize)} -> ${formatBytes(
        minifiedSize
      )} (${getPercentChange(originalSize, minifiedSize)})`
    );

    fs.writeFileSync(outputPath, output.styles);
    console.log(`    Output: ${outputPath}`);
  }

  console.log("CSS build complete!");
}

function formatBytes(bytes) {
  if (bytes < 1024) return bytes + " B";
  return (bytes / 1024).toFixed(1) + " KB";
}

function getPercentChange(original, final) {
  const change = (((final - original) / original) * 100).toFixed(1);
  return change > 0 ? `+${change}%` : `${change}%`;
}

buildCss();
