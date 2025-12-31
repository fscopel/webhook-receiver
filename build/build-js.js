/**
 * JavaScript Build Script
 * Minifies with esbuild, then obfuscates with javascript-obfuscator
 */

const fs = require("fs");
const path = require("path");
const esbuild = require("esbuild");
const JavaScriptObfuscator = require("javascript-obfuscator");
const config = require("./config");

async function buildJs() {
  console.log("Building JavaScript files...");

  for (const file of config.jsFiles) {
    const inputPath = path.join(config.SRC_DIR, file);
    const outputPath = path.join(config.DIST_DIR, file);

    if (!fs.existsSync(inputPath)) {
      console.warn(`Warning: ${inputPath} not found, skipping...`);
      continue;
    }

    console.log(`  Processing: ${file}`);

    // Step 1: Minify with esbuild
    const minified = await esbuild.build({
      entryPoints: [inputPath],
      bundle: false,
      minify: true,
      write: false,
      target: ["es2020"],
      format: "iife",
    });

    const minifiedCode = minified.outputFiles[0].text;
    const originalSize = fs.statSync(inputPath).size;
    const minifiedSize = Buffer.byteLength(minifiedCode, "utf8");

    console.log(
      `    Minified: ${formatBytes(originalSize)} -> ${formatBytes(
        minifiedSize
      )}`
    );

    // Step 2: Obfuscate
    const obfuscationResult = JavaScriptObfuscator.obfuscate(
      minifiedCode,
      config.obfuscatorOptions
    );
    const obfuscatedCode = obfuscationResult.getObfuscatedCode();
    const obfuscatedSize = Buffer.byteLength(obfuscatedCode, "utf8");

    console.log(
      `    Obfuscated: ${formatBytes(obfuscatedSize)} (${getPercentChange(
        originalSize,
        obfuscatedSize
      )})`
    );

    // Write output
    fs.writeFileSync(outputPath, obfuscatedCode);
    console.log(`    Output: ${outputPath}`);
  }

  console.log("JavaScript build complete!");
}

function formatBytes(bytes) {
  if (bytes < 1024) return bytes + " B";
  return (bytes / 1024).toFixed(1) + " KB";
}

function getPercentChange(original, final) {
  const change = (((final - original) / original) * 100).toFixed(1);
  return change > 0 ? `+${change}%` : `${change}%`;
}

buildJs().catch((err) => {
  console.error("JavaScript build failed:", err);
  process.exit(1);
});
