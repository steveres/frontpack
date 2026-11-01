// Local development server: serves the website on http://localhost:3000, straight from its parts
// (see parts.mjs) laid over one another, with the build's own files (app-config.json, vendor/) on top.
// In Azure the packaged site (build.mjs --out) is served by Static Web Apps.
//   node server.js            serve
//   import { start } from …   used by build.mjs --serve
import express from 'express';
import { fileURLToPath } from 'node:url';
import { GENERATED, parts } from './parts.mjs';

const PORT = Number(process.env.PORT ?? 3000);

/** Starts serving; rejects (without side effects) if the port is taken. */
export function start() {
  const app = express();
  app.use(express.static(GENERATED, { extensions: ['html'] }));
  for (const part of parts()) {
    app.use(express.static(part.site, { extensions: ['html'] }));
    app.use('/js', express.static(part.js));
  }
  return new Promise((resolve, reject) => {
    app.listen(PORT, err => {
      if (err) {
        reject(err.code === 'EADDRINUSE' ? new Error(`Port ${PORT} is already in use (is another "npm run dev" running?). Stop it or set PORT.`) : err);
        return;
      }
      console.log(`Serving the website on http://localhost:${PORT}`);
      resolve();
    });
  });
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  start().catch(err => { console.error(err.message ?? err); process.exit(1); });
}
