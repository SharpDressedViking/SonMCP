// eslint.config.js

import js from "@eslint/js";
import globals from "globals";
import tseslint from "typescript-eslint";

export default [

    /*
     * Base JavaScript recommended rules
     */
    js.configs.recommended,

    /*
     * TypeScript support (safe even if TS not used)
     */
    ...tseslint.configs.recommended,

    /*
     * Universal project defaults
     */
    {
        files: ["**/*.{js,mjs,cjs,ts,jsx,tsx}"],

        languageOptions: {
            ecmaVersion: "latest",
            sourceType: "module",

            globals: {
                ...globals.browser,
                ...globals.node
            }
        },

        rules: {
            /*
             * Possible errors
             */
            "no-debugger": "warn",
            "no-console": "off",

            /*
             * Correctness over style
             */
            "no-unused-vars": "off", // handled by TS rule
            "@typescript-eslint/no-unused-vars": [
                "warn",
                {
                    argsIgnorePattern: "^_",
                    varsIgnorePattern: "^_"
                }
            ],

            /*
             * Modern JS expectations
             */
            "prefer-const": "warn",
            "no-var": "error",

            /*
             * Prevent common async bugs (JS only)
             */
            "require-await": "warn",

            /*
             * Safer equality
             */
            "eqeqeq": ["warn", "smart"],

            /*
             * Reduce accidental complexity
             */
            "no-empty": ["warn", { allowEmptyCatch: true }]
        }
    },

    /*
     * Ignore common generated output
     */
    {
        ignores: [
            "**/node_modules/**",
            "**/dist/**",
            "**/build/**",
            "**/.next/**",
            "**/coverage/**",
            "**/*.min.js"
        ]
    }
];