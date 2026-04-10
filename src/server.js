const https = require('https');
const fs = require('fs');
const path = require('path');
const express = require('express');

const app = express();
const PORT = 3000;

// Serve static files from project root
app.use(express.static(__dirname));

const options = {
    key: fs.readFileSync('localhost.key'),
    cert: fs.readFileSync('localhost.crt')
};

https.createServer(options, app)
    .listen(PORT, () => {
        console.log(`Secure server running on https://localhost:${PORT}`);
    });