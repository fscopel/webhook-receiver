/**
 * Build Configuration
 * Central configuration for the minification and obfuscation pipeline
 */

const path = require("path");

const ROOT_DIR = path.resolve(__dirname, "..");
const SRC_DIR = path.join(ROOT_DIR, "src", "wwwroot");
const DIST_DIR = path.join(ROOT_DIR, "src", "wwwroot-dist");

module.exports = {
  // Paths
  ROOT_DIR,
  SRC_DIR,
  DIST_DIR,

  // JavaScript files to process
  jsFiles: ["js/app.js", "js/auth.js"],

  // CSS files to process
  cssFiles: ["css/styles.css"],

  // HTML files to process
  htmlFiles: ["index.html", "login.html", "404.html"],

  // Files/folders to copy without processing
  copyAssets: [
    // Add any static assets like images, fonts here
    // 'images/',
    // 'fonts/'
  ],

  // JavaScript Obfuscator options
  // See: https://github.com/javascript-obfuscator/javascript-obfuscator
  obfuscatorOptions: {
    // Compact output (no line breaks)
    compact: true,

    // Control flow flattening makes code harder to follow
    controlFlowFlattening: true,
    controlFlowFlatteningThreshold: 0.75,

    // Dead code injection adds fake code
    deadCodeInjection: true,
    deadCodeInjectionThreshold: 0.4,

    // Debug protection prevents DevTools debugging
    debugProtection: false, // Enable for maximum protection, but can cause issues
    debugProtectionInterval: 0,

    // Disable console output
    disableConsoleOutput: false, // Set true to remove all console.log in production

    // Identifier renaming
    identifierNamesGenerator: "hexadecimal",

    // Log level
    log: false,

    // Numbers to expressions (1 becomes 0x1)
    numbersToExpressions: true,

    // Rename globals
    renameGlobals: false, // Keep false to avoid breaking external library calls

    // Self defending (breaks if code is formatted)
    selfDefending: true,

    // Shuffle string array
    shuffleStringArray: true,

    // Split strings into chunks
    splitStrings: true,
    splitStringsChunkLength: 10,

    // String array encoding
    stringArray: true,
    stringArrayCallsTransform: true,
    stringArrayCallsTransformThreshold: 0.75,
    stringArrayEncoding: ["base64"],
    stringArrayIndexShift: true,
    stringArrayRotate: true,
    stringArrayShuffle: true,
    stringArrayWrappersCount: 2,
    stringArrayWrappersChainedCalls: true,
    stringArrayWrappersParametersMaxCount: 4,
    stringArrayWrappersType: "function",
    stringArrayThreshold: 0.75,

    // Transform object keys
    transformObjectKeys: true,

    // Unicode escape sequence
    unicodeEscapeSequence: false,
  },

  // HTML Minifier options
  htmlMinifierOptions: {
    collapseWhitespace: true,
    removeComments: true,
    removeRedundantAttributes: true,
    removeScriptTypeAttributes: true,
    removeStyleLinkTypeAttributes: true,
    useShortDoctype: true,
    minifyCSS: true,
    minifyJS: true,
  },

  // Clean CSS options
  cleanCssOptions: {
    level: {
      1: {
        specialComments: 0,
      },
      2: {
        restructureRules: true,
      },
    },
  },
};
