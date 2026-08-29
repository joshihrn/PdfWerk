import { defineConfig } from 'vite'

/**
 * Builds the embeddable widget as a standalone, dependency-free bundle.
 *
 * Emitted as IIFE so a plain <script src> works with no module loader, no build step and no
 * framework on the host page — which is the entire point of the embed.
 */
export default defineConfig({
  build: {
    outDir: 'public',
    emptyOutDir: false,
    lib: {
      entry: 'embed/index.ts',
      name: 'PdfWerk',
      formats: ['iife'],
      fileName: () => 'pdfwerk-embed.js',
    },
    // Vite 8 minifies with oxc; naming esbuild explicitly would require it as a separate
    // dependency for no benefit here.
    minify: true,
  },
})
